# High-Scale System Architecture & Scaling Strategy

## 1. Stateless Application Tier Scaling
Backend ASP.NET Core được thiết kế hoàn toàn **Stateless**:
- Không sử dụng In-Memory Session của web server (không dùng `HttpContext.Session` in-memory).
- Không lưu file tải lên trên đĩa cứng cục bộ của container (toàn bộ đẩy lên MinIO / AWS S3).
- Không giữ WebSocket connection state trên RAM mà không có Redis Backplane.

### Scaling Mechanism:
- Khi CPU/Memory của Pod đạt ngưỡng 70%, Kubernetes HPA (Horizontal Pod Autoscaler) tự động scale số lượng Pod từ `min: 3` lên tới `max: 30+`.
- Nginx / Kubernetes Ingress Controller phân phối tải đồng đều (Round-Robin hoặc Least-Connections) tới tất cả các Pod đang ở trạng thái Ready.

---

## 2. Database Scaling: Primary & Read-Replicas

### 2.1. Read-Write Splitting Architecture
Lưu lượng E-Commerce có tỷ lệ Đọc/Ghi trung bình là **85% Đọc - 15% Ghi**:
- **PostgreSQL Primary:** Xử lý 100% các lệnh Ghi (`INSERT`, `UPDATE`, `DELETE`), cùng với các giao dịch tài chính yêu cầu ACID tức thời (Tạo đơn, Trừ kho, Thanh toán).
- **PostgreSQL Read Replicas (1 - 3 instances):** Nhận dữ liệu bất đồng bộ qua Streaming Replication để phục vụ các truy vấn đọc nặng: Lịch sử đơn hàng, xem chi tiết sản phẩm khi cache miss, báo cáo thống kê.

### 2.2. The Read-After-Write Consistency Solution
Một vấn đề thường gặp với Read Replicas là **Replication Lag** (độ trễ nhân bản từ vài chục ms đến vài giây). Nếu user vừa nhấn "Đặt hàng", sau đó trang web tự chuyển hướng sang "Chi tiết đơn hàng" mà query đọc trỏ vào Replica chưa kịp sync, khách hàng sẽ thấy lỗi 404 "Không tìm thấy đơn hàng".

### Giải pháp kỹ thuật:
- Sử dụng Session Token / Sticky Cookie đánh dấu thời điểm ghi cuối cùng của user: `last_write_timestamp`.
- Nếu `Now - last_write_timestamp < 5 seconds`, DbContext tự động định tuyến truy vấn đọc về **PostgreSQL Primary**.
- Sau 5 giây, truy vấn được an toàn chuyển hướng sang các **Read Replicas**.

---

## 3. Database Connection Pool Sizing
Anti-pattern phổ biến là cấp quá nhiều connection (`MaxPoolSize = 1000`), dẫn đến PostgreSQL dành phần lớn thời gian để Context-Switching giữa các tiến trình OS thay vì thực thi query.

### Công thức tính toán Connection Pool chuẩn (PostgreSQL Formula):
$$\text{Max Connections} = (\text{CPU Cores} \times 2) + \text{Effective Spindle Count}$$
- Với một server PostgreSQL 16 vCPU và SSD NVMe: Kích thước connection pool tối ưu trên server chỉ nên dao động từ **32 đến 64 kết nối**.
- Ở tầng trung gian giữa hàng chục Pod Backend và PostgreSQL, bắt buộc triển khai **PgBouncer** ở chế độ `Transaction Pooling`. Mỗi Pod giữ một pool nhỏ (10-20 kết nối) tới PgBouncer, PgBouncer tái sử dụng số kết nối thực xuống DB một cách cực kỳ hiệu quả.

---

## 4. Flash Sale Tier: Surviving 100,000 Concurrent Users

```
Traffic: 100,000 requests/s
       ↓
[ Cloudflare CDN & WAF ] (Chặn bot, rate-limit theo IP, phục vụ static assets)
       ↓ (Còn ~30,000 req/s hợp lệ)
[ Ingress Controller / Nginx ]
       ↓
[ ASP.NET Core API Cluster (15 Pods) ]
       ↓
[ Redis Cluster - Atomic Stock Token Lua Script ]
       ├── 99.9% Requests: Nhận "HẾT HÀNG" trong 2ms (Zero DB load)
       └── 100 Requests: Nhận Token Đặt hàng thành công
                ↓
       [ Apache Kafka (Topic: flash-sale-orders) ]
                ↓
       [ Background Worker ] -> [ PostgreSQL (Trừ kho DB & Tạo đơn hàng) ]
```

### Phân tích Trade-off:
- **Ưu điểm:** Hệ thống cơ sở dữ liệu PostgreSQL hoàn toàn không bị ảnh hưởng, thời gian phản hồi cho 100,000 user cực nhanh (< 50ms), không hề có nguy cơ overselling.
- **Trade-off:** Chấp nhận tính bất đồng bộ (Asynchronous Order Placement) - Người dùng nhận thông báo "Đơn hàng đang được xếp hàng xử lý", sau 2-5 giây thông báo hoàn tất sẽ được đẩy qua WebSocket / Notification.
