# ADR-003: Redis Strategy - Distributed Caching, Shopping Cart, Atomic Flash Sale, and Anti-Stampede

## Status
Accepted

## Context
Trong hệ thống E-Commerce quy mô lớn, lưu lượng đọc (Read traffic) cho Product Catalog, Categories và Cart thường gấp 10 - 50 lần lưu lượng ghi (Write traffic).
Nếu tất cả các yêu cầu đọc đều query trực tiếp vào PostgreSQL, database sẽ nhanh chóng bị cạn kiệt connection pool và quá tải CPU.
Tuy nhiên, lạm dụng Redis hoặc thiết kế caching cẩu thả sẽ dẫn đến:
1. **Cache Stampede (Thundering Herd):** Khi một hot key (ví dụ sản phẩm flash sale) hết hạn, hàng chục nghìn request đồng thời lao vào PostgreSQL để query và ghi cache.
2. **Cache Penetration:** Hacker hoặc bot liên tục truy vấn các ID không tồn tại (`id = -99999`) khiến cache luôn miss và database phải quét liên tục.
3. **Inconsistent Data:** Cập nhật DB nhưng không xóa cache hoặc race condition trong quá trình invalidate cache khiến người dùng nhìn thấy giá cũ.
4. **Data Loss:** Coi Redis là Single Source of Truth cho dữ liệu tài chính hoặc đơn hàng.

## Decision
Chúng tôi quyết định ứng dụng **Redis** theo các nguyên tắc kỹ thuật chuẩn production:

1. **Role of Redis:**
   - **L2 Cache:** Cache-Aside cho Product Catalog, Categories, SKU details.
   - **Fast Session / Cart Store:** Giỏ hàng người dùng được lưu trữ chính trên Redis Hash (`cart:user:{userId}`) để đạt độ trễ < 1ms, định kỳ đồng bộ backup vào PostgreSQL.
   - **Rate Limiting:** Sliding Window Rate Limiter dựa trên Redis Sorted Sets (`rate-limit:{ip_or_userId}`).
   - **Atomic Token Bucket for Flash Sale:** Giữ stock token trên Redis thông qua Lua script để chặn hàng chục nghìn request trước khi chúng chạm tới PostgreSQL.

2. **Cache Key Naming Convention:**
   - Định dạng chuẩn có tiền tố và phân cấp: `{module}:{entity}:{identifier}`
   - Ví dụ:
     - `product:id:{productId}`
     - `product:sku:{skuCode}`
     - `cart:user:{userId}`
     - `ratelimit:{ip}:{endpoint}`
     - `lock:inventory:{skuId}`

3. **Protection Mechanisms:**
   - **Cache Stampede Protection:** Áp dụng **Probabilistic Early Expiration (XFetch algorithm)** hoặc Distributed Mutex Lock (Redlock / single instance lock với spin wait ngắn). Chỉ đúng 1 thread được quyền query DB và update cache khi key hết hạn, các request khác chờ hoặc nhận stale data tạm thời.
   - **Cache Penetration Protection:** Lưu trữ `Null Value` (Empty Object) với TTL ngắn (1 - 2 phút) khi query DB không tìm thấy bản ghi.
   - **Cache Invalidation:** Áp dụng mô hình **Write-Through Invalidation**:
     1. Ghi dữ liệu vào PostgreSQL trong transaction.
     2. Sau khi commit transaction, xóa key tương ứng trên Redis (`DEL product:id:{id}`).
     3. Đồng thời xuất bản `ProductUpdatedDomainEvent` lên Kafka để các read-replica / search-service đồng bộ.

4. **Source of Truth Invariant:**
   - PostgreSQL **luôn là Single Source of Truth**. Redis chỉ là tầng tăng tốc. Nếu Redis down, hệ thống tự động fallback truy vấn PostgreSQL an toàn thông qua Circuit Breaker (Polly).

## Consequences
### Positive
- Giảm tải tới 90-95% query đọc xuống PostgreSQL.
- Phản hồi cực nhanh (< 2ms) cho giỏ hàng và tra cứu catalog.
- Triệt tiêu hoàn toàn rủi ro sập database khi có sự kiện khuyến mãi hot key.

### Negative / Trade-offs
- Phải quản lý thêm một hạ tầng phân tán (Redis Cluster/Sentinel).
- Chấp nhận tính nhất quán cuối cùng (Eventual Consistency) ở mức mili-giây giữa DB và Cache.
