# ADR-002: PostgreSQL as Primary Datastore with Schema Separation & Read-Write Segregation

## Status
Accepted

## Context
Hệ thống E-Commerce cần lưu trữ các thực thể có tính toàn vẹn cao (Tài khoản, Đơn hàng, Giao dịch thanh toán, Tồn kho) yêu cầu tính chất ACID mạnh mẽ.
Một số hệ thống áp dụng NoSQL (MongoDB, DynamoDB) cho toàn bộ dữ liệu, nhưng gặp phải các hạn chế nghiêm trọng về giao dịch đa tài liệu (multi-document transactions), thiếu check constraints, thiếu atomic update có điều kiện phức tạp để chống oversell, và rủi ro data corruption khi hệ thống mở rộng.

Mặt khác, nếu tất cả các module dùng chung schema `public` của cơ sở dữ liệu quan hệ, việc tạo quan hệ foreign key tùy tiện giữa các bảng (ví dụ: `orders.user_id` FK sang `users.id`) sẽ làm mất tính độc lập của domain và gây khóa chết (lock contention) trên các bảng dùng chung.

## Decision
Chúng tôi chọn **PostgreSQL** làm Single Source of Truth cho toàn bộ nghiệp vụ cốt lõi, đi kèm các quy định kỹ thuật:

1. **Schema-per-Module Separation:**
   - Mỗi Bounded Context sở hữu một schema riêng:
     - `identity.*`
     - `product.*`
     - `inventory.*`
     - `cart.*`
     - `orders.*`
     - `payment.*`
     - `promotion.*`
     - `shipping.*`
     - `review.*`
     - `notification.*`
     - `common.*` (bảng `outbox_events`, `idempotency_keys`, `audit_logs`)
   - Tuyệt đối **không** tạo foreign key vật lý xuyên schema. Tham chiếu giữa các schema chỉ là các giá trị UUID/ID nguyên thủy.

2. **Strict Integrity Constraints:**
   - Sử dụng triệt để `CHECK` constraints (ví dụ: `available_quantity >= 0`, `price >= 0`, `discount_percent BETWEEN 0 AND 100`).
   - Sử dụng `UNIQUE` constraint kết hợp partial index (ví dụ: `UNIQUE(sku_id, warehouse_id)`).

3. **Read-Write Segregation Architecture:**
   - Tách biệt `IWriteDbContext` (kết nối PostgreSQL Primary) và `IReadDbContext` (kết nối PostgreSQL Read Replicas qua Load Balancer/PgBouncer).
   - Áp dụng nguyên tắc **Read-After-Write Consistency**: Khi một client thực hiện hành vi thay đổi dữ liệu (như vừa cập nhật Profile hoặc tạo Order), các truy vấn chi tiết tiếp theo trong vòng 5 giây sẽ tự động đọc từ Primary thay vì Replica để tránh replication lag.

4. **Connection Pooling Optimization:**
   - Sử dụng Npgsql connection pooling tích hợp hoặc PgBouncer ở tầng hạ tầng để kiểm soát số lượng kết nối tối đa, tránh nghẽn RAM/CPU trên PostgreSQL cluster.

## Consequences
### Positive
- **Độ tin cậy tuyệt đối:** ACID transaction bảo vệ dữ liệu tài chính và tồn kho.
- **Ranh giới vật lý rõ ràng:** Từng schema có thể dễ dàng được dump/restore hoặc di dời sang một PostgreSQL instance riêng khi tách microservices.
- **Tối ưu hóa Index:** Cho phép đánh các index chuyên biệt (B-tree, GIN cho JSONB metadata, Partial Index cho trạng thái pending) mà không gây overhead cho các module khác.

### Negative / Trade-offs
- Không thể dùng SQL JOIN xuyên schema giữa các bảng của các module khác nhau. Khi cần tổng hợp dữ liệu (ví dụ hiển thị tên sản phẩm trong chi tiết đơn hàng), hệ thống phải snapshot thông tin tại thời điểm mua (Order Item snapshot) hoặc truy vấn in-memory/eventually consistent qua search index.
