# Performance & Load Testing Specification with k6

## 1. Load Testing Objectives & KPIs

Mục tiêu của kiểm thử tải là xác định giới hạn chịu tải tối đa, điểm nghẽn cổ chai (bottlenecks) và chứng minh hệ thống không bị lỗi khi chịu áp lực concurrency lớn:

| Kịch bản | Số lượng Virtual Users (VUs) | Lưu lượng kỳ vọng (RPS) | P95 Latency | P99 Latency | Tỷ lệ lỗi (Error Rate) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **1. Catalog Browsing** | 1,000 | 5,000 req/s | < 50ms | < 100ms | 0.00% |
| **2. Product Search** | 500 | 2,500 req/s | < 80ms | < 150ms | < 0.01% |
| **3. Add to Cart** | 500 | 2,000 req/s | < 30ms | < 60ms | 0.00% |
| **4. Concurrent Checkout** | 1,000 | 1,000 req/s | < 250ms | < 500ms | 0.00% (Zero Oversell) |
| **5. Flash Sale Spike** | 10,000 (ramp in 5s) | 15,000 req/s | < 100ms | < 250ms | 0.00% Crash / 0% Oversell |

---

## 2. Flash Sale Concurrency Invariant Test

Kịch bản quan trọng nhất: **1 SKU duy nhất có 100 sản phẩm trong kho. 10,000 VUs cùng gửi request mua trong cùng 5 giây**.

### Điều kiện nghiệm thu (Acceptance Criteria):
1. **Tuyệt đối không bán vượt:**
   $$\text{Số lượng Reservation thành công} \equiv 100$$
   $$\text{Tồn kho cuối cùng trong DB} \equiv 0$$
   $$\text{Không bao giờ có tồn kho âm } (< 0)$$
2. **PostgreSQL Server không bị crash:**
   Connection pool sử dụng không vượt quá giới hạn tối đa (`MaxPoolSize`).
3. **Idempotency hoạt động:**
   Nếu k6 gửi lại cùng một `Idempotency-Key` 100 lần, chỉ có đúng 1 đơn hàng được tạo, 99 lần còn lại trả về cùng response mà không trừ thêm kho.

---

## 3. k6 Test Script Architecture

Các script được tổ chức trong thư mục `tests/load/`:
- `scenarios/browsing.js`: Duyệt danh mục và chi tiết sản phẩm.
- `scenarios/search.js`: Tìm kiếm full-text với bộ lọc giá và thuộc tính.
- `scenarios/cart.js`: Thêm, sửa số lượng, xóa giỏ hàng.
- `scenarios/checkout.js`: Quy trình checkout hoàn chỉnh (Auth -> Cart -> Order -> Payment).
- `scenarios/flash-sale.js`: Bắn đồng thời hàng nghìn request tranh mua 1 SKU.

### Lệnh thực thi:
```bash
# Chạy kịch bản Flash Sale với k6
k6 run tests/load/scenarios/flash-sale.js --vus 1000 --duration 30s
```
