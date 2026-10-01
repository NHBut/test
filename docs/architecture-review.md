# Comprehensive Architecture Review & Production Stress Analysis

## 1. System Evaluation & Resilience Matrix

| Thành phần | Điểm nghẽn tiềm tàng (Bottleneck) | Biện pháp giảm thiểu (Mitigation Strategy) | Single Point of Failure (SPOF)? |
| :--- | :--- | :--- | :--- |
| **API Host Tier** | Bão hòa CPU khi hash mật khẩu hoặc xử lý JSON serialize lớn | Scale ngang vô hạn với Kubernetes HPA (3 -> 30+ pods), tải chia đều qua Ingress | **KHÔNG** (Stateless hoàn toàn) |
| **PostgreSQL Database** | Giới hạn IOPS đĩa và nghẽn Connection Pool khi có hàng chục nghìn kết nối đồng thời | Phân tách schema, Read-Write Splitting (1 Primary + 3 Replicas), PgBouncer Transaction Pooling | **CÓ** nếu chỉ có 1 Primary (Cần Patroni HA / AWS Aurora Multi-AZ) |
| **Redis Cache** | Cạn kiệt RAM (OOM) nếu không có eviction policy hoặc Cache Stampede | Redis Cluster phân mảnh (Sharding), Redis Sentinel tự động failover, Stampede Lock | **KHÔNG** (Dùng Cluster mode 3 Masters - 3 Slaves) |
| **Apache Kafka** | Consumer lag tăng cao nếu downstream xử lý chậm hoặc partition quá ít | Partitioning theo Entity ID (16-32 partitions), Horizontal Consumer Auto-scaling qua KEDA | **KHÔNG** (Cụm KRaft tối thiểu 3 Controllers/Brokers, replication-factor: 3) |
| **OpenSearch** | JVM Heap pressure khi chạy aggregation đa chiều phức tạp | Dedicated Master Nodes và Data Nodes, bulk indexing bất đồng bộ, refresh interval 5s | **KHÔNG** (Cụm Multi-Node với Replica Shards) |

---

## 2. Answers to Critical High-Load & Failure Questions

### Q1: "Điều gì xảy ra nếu traffic tăng 10x?"
- **Hành vi hệ thống:** Lưu lượng từ 2,000 req/s tăng lên 20,000 req/s.
- **Phản ứng:**
  1. **Tầng API:** Kubernetes HPA phát hiện CPU vượt 70%, lập tức scale Pods từ 3 lên 15 pods trong 60 giây.
  2. **Tầng Cache:** 90% traffic đọc (Product Catalog, Categories, Cart) được Redis hấp thụ trọn vẹn, độ trễ phản hồi vẫn duy trì < 5ms.
  3. **Tầng Database:** Tỷ lệ đọc xuống DB được san tải sang 3 PostgreSQL Read-Replicas. Primary chỉ xử lý ~2,000 write ops/s, nằm an toàn trong giới hạn của PgBouncer.
  4. **Tầng Messaging:** Transactional Outbox ghi nhận sự kiện vào DB, worker đẩy đều đặn lên Kafka mà không làm nghẽn API.

---

### Q2: "Điều gì xảy ra nếu traffic tăng 100x?"
- **Hành vi hệ thống:** Lưu lượng tăng vọt lên 200,000 req/s (Chiến dịch Mega Sale toàn sàn).
- **Phản ứng:**
  1. **Edge CDN (Cloudflare):** Bật chế độ "Under Attack Mode", kích hoạt Web Application Firewall (WAF), cache toàn bộ static assets và GET requests danh mục công khai tại Edge.
  2. **Rate Limiting:** Nginx Ingress và Middleware giới hạn 100 req/min trên mỗi User/IP. Bot và crawler bị drop gói tin ngay tại cửa ngõ.
  3. **Graceful Degradation:** Tạm thời tắt các tác vụ nặng không thiết yếu (tính năng gợi ý sản phẩm AI, đồng bộ review tức thì).
  4. **Kafka Buffer:** Các tác vụ gửi email thông báo, xuất hóa đơn, tích lũy điểm thưởng được xếp hàng trên Kafka và xử lý chậm lại dần mà không làm sập luồng mua hàng chính.

---

### Q3: "Điều gì xảy ra nếu Redis chết?"
- **Rủi ro:** Nguy cơ Thundering Herd đánh sập PostgreSQL.
- **Cơ chế phòng vệ:**
  1. Triển khai **Circuit Breaker (Polly)** tại `RedisCacheService`. Khi phát hiện Redis connection timeout liên tiếp 5 lần, Circuit Breaker lập tức mở (Open state).
  2. Ứng dụng tự động fallback truy vấn trực tiếp PostgreSQL với cơ chế Semaphore giới hạn số query đồng thời (Concurrency Throttling) để bảo vệ DB.
  3. Giỏ hàng (Cart) tự động fallback đọc từ bảng dự phòng trong schema `cart` trên PostgreSQL.
  4. Sau khi cụm Redis Sentinel hoàn thành failover sang Replica node mới, Circuit Breaker tự động đóng (Close state) và khôi phục hoạt động bình thường.

---

### Q4: "Điều gì xảy ra nếu Kafka chết?"
- **Rủi ro:** Không thể xuất bản hoặc tiêu thụ message bất đồng bộ.
- **Cơ chế phòng vệ:**
  1. Nhờ **Transactional Outbox Pattern**, API đặt hàng và thanh toán **HOÀN TOÀN KHÔNG BỊ GIÁN ĐOẠN**. Đơn hàng vẫn được tạo thành công, dữ liệu outbox được lưu an toàn trong bảng `common.outbox_events` của PostgreSQL.
  2. Background Worker phát hiện Kafka ngắt kết nối sẽ áp dụng Exponential Backoff và tạm dừng quét outbox.
  3. Khi Kafka cluster phục hồi: Worker tự động tiếp tục quét các event pending theo thứ tự thời gian (`ORDER BY created_at ASC FOR UPDATE SKIP LOCKED`) và publish bù. Không một thông điệp nào bị mất mát!

---

### Q5: "Điều gì xảy ra nếu PostgreSQL read replica lag?"
- **Rủi ro:** User vừa đặt đơn hàng xong, chuyển hướng sang màn hình xem đơn hàng nhưng nhận lỗi "Đơn hàng không tồn tại" do Replica chưa kịp sync.
- **Cơ chế phòng vệ:**
  - Áp dụng nguyên tắc **Read-After-Write Consistency**:
  - Khi user thực hiện mutating request (`POST /orders`, `PUT /profile`), hệ thống gắn cookie/header phiên `last_write_timestamp`.
  - Trong vòng 5 giây tiếp theo, mọi truy vấn chi tiết từ user đó được bắt buộc định tuyến về **PostgreSQL Primary** thay vì Replica.
  - Sau 5 giây, khi replica đã đồng bộ xong, truy vấn lại được trỏ về Replica an toàn.

---

### Q6: "Điều gì xảy ra nếu payment gateway timeout?"
- **Rủi ro:** Client chờ kết nối bị treo hoặc mạng chập chờn không nhận được phản hồi thanh toán.
- **Cơ chế phòng vệ:**
  1. Ứng dụng tuyệt đối **không bao giờ đồng bộ chờ kết quả thanh toán** trong luồng HTTP của client.
  2. Đơn hàng luôn được lưu ở trạng thái `PENDING_PAYMENT` với thời hạn thanh toán 15 phút.
  3. Kết quả thanh toán chỉ được chốt thông qua **Server-to-Server Webhook** bất đồng bộ có chữ ký HMAC.
  4. Nếu sau 15 phút không nhận được webhook: `InventoryReservationExpiryWorker` tự động giải phóng kho hàng và hủy đơn hàng một cách an toàn.

---

### Q7: "Điều gì xảy ra nếu user gửi cùng request 100 lần?"
- **Rủi ro:** Bấm nút "Đặt hàng" hoặc "Thanh toán" liên tục gây tạo 100 đơn hàng trùng lặp.
- **Cơ chế phòng vệ:**
  1. **Idempotency-Key Middleware:** Client bắt buộc gửi kèm header `Idempotency-Key: <UUID>`.
  2. Request đầu tiên: Ghi nhận key vào `common.idempotency_keys` với trạng thái `PROCESSING`.
  3. 99 request tiếp theo:
     - Nếu request đang in-flight: Trả mã `409 Conflict ("Request is already in flight. Please wait.")`.
     - Sau khi request đầu tiên hoàn tất: 99 request còn lại lập tức nhận lại kết quả HTTP 201/200 đã lưu trong cache mà **không chạy lại business logic một lần nào nữa**.
  4. Số lượng đơn hàng được tạo: **Đúng 1 đơn hàng duy nhất!**

---

### Q8: "Điều gì xảy ra nếu Kafka gửi cùng event 2 lần?"
- **Rủi ro:** Duplicate fulfillment, xuất kho 2 lần hoặc gửi 2 email cho khách.
- **Cơ chế phòng vệ:**
  1. Triển khai **Idempotent Consumer Pattern** thông qua bảng `common.processed_events`.
  2. Mỗi message Kafka đều có header `message-id`.
  3. Trước khi thực thi logic, consumer kiểm tra:
     ```sql
     SELECT 1 FROM common.processed_events WHERE event_id = @id AND consumer_group = @group;
     ```
  4. Nếu đã tồn tại: Bỏ qua message ngay lập tức và commit offset.
  5. Nếu chưa tồn tại: Thực thi xử lý và chèn `event_id` vào bảng trong cùng một transaction. Đảm bảo nghiệp vụ chỉ thực thi đúng 1 lần duy nhất (**Effectively Once Execution**).

---

### Q9: "Điều gì xảy ra nếu 100,000 user cùng mua SKU có stock = 100?"
- **Rủi ro lớn nhất:** Overselling và sập database do DDoS.
- **Cơ chế phòng vệ đa tầng:**
  1. **Tầng 1 (Rate Limit & CDN):** Lọc bot và spam, giới hạn tần suất request.
  2. **Tầng 2 (Redis Atomic Token via Lua Script):**
     - Tồn kho 100 sản phẩm được nạp sẵn vào Redis: `flash:sku:{skuId}:stock = 100`.
     - 100,000 request được xử lý bởi Lua script nguyên tử trong Redis Engine.
     - **Chỉ đúng 100 request đầu tiên** nhận return code `1` (Được cấp vé mua).
     - **99,900 request còn lại** nhận phản hồi `409 Conflict (Hết hàng)` ngay tức thì ở tầng Redis (< 2ms).
  3. **Tầng 3 (PostgreSQL Atomic Conditional Update):**
     - 100 request hợp lệ được đưa vào xử lý qua câu lệnh:
       ```sql
       UPDATE inventory.product_inventories
       SET available_quantity = available_quantity - 1,
           reserved_quantity = reserved_quantity + 1
       WHERE sku_id = @skuId AND available_quantity >= 1;
       ```
     - Cơ sở dữ liệu chỉ phải chịu tải đúng 100 transaction cực nhẹ.
  4. **Kết quả cuối cùng:**
     - **Số lượng bán ra:** Đúng chính xác 100 sản phẩm.
     - **Tồn kho âm:** 0 (Không bao giờ xảy ra oversell).
     - **Tình trạng PostgreSQL:** Hoạt động ổn định ở mức CPU < 15%.
