# ADR-005: Transactional Outbox Pattern to Guarantee At-Least-Once Delivery

## Status
Accepted

## Context
Trong bất kỳ hệ thống phân tán hay Event-Driven Architecture nào, việc thực hiện đồng thời 2 thao tác:
1. Ghi dữ liệu vào Database (`INSERT INTO orders ...`)
2. Publish event lên Message Broker (`Kafka.ProduceAsync("order-events", ...)`)
được gọi là bài toán **Dual-Write Problem**.

Nếu làm ngây thơ:
- Kịch bản A: DB commit thành công -> Kafka publish lỗi (mất mạng, broker crash) => Đơn hàng có trong DB nhưng không có event nào được bắn đi, hàng không được trừ kho, email không gửi, search không sync.
- Kịch bản B: Kafka publish trước thành công -> DB commit thất bại (vi phạm constraint, deadlock) => Khách hàng chưa có đơn hàng nhưng kho đã bị trừ hoặc email xác nhận đã gửi đi.

Dùng Two-Phase Commit (2PC / XA Transactions) giữa RDBMS và Kafka không khả thi trên thực tế vì hiệu năng cực thấp, dễ gây deadlock và Kafka không hỗ trợ chuẩn XA với PostgreSQL.

## Decision
Chúng tôi quyết định áp dụng **Transactional Outbox Pattern** với bảng trung gian `common.outbox_events`:

1. **Atomic Local Transaction:**
   Mọi mutating command (Tạo đơn, Cập nhật trạng thái, Hủy đơn) bắt buộc phải ghi dữ liệu nghiệp vụ và sự kiện Outbox trong **CÙNG MỘT DATABASE TRANSACTION**:
   ```sql
   BEGIN TRANSACTION;
   INSERT INTO orders.orders (id, customer_id, total_amount, status) VALUES (...);
   INSERT INTO common.outbox_events (id, aggregate_type, aggregate_id, event_type, payload, status, created_at)
   VALUES (gen_random_uuid(), 'Order', @orderId, 'OrderCreated', @jsonPayload, 'PENDING', NOW());
   COMMIT;
   ```

2. **Outbox Event Publisher (Background Worker):**
   - Một background service (`OutboxProcessor`) quét các sự kiện chưa gửi với truy vấn tối ưu cao:
     ```sql
     SELECT id, aggregate_id, event_type, payload 
     FROM common.outbox_events 
     WHERE status = 'PENDING' 
     ORDER BY created_at ASC 
     LIMIT 100 
     FOR UPDATE SKIP LOCKED;
     ```
   - Cơ chế `FOR UPDATE SKIP LOCKED` cho phép nhiều pod/instance worker chạy đồng thời mà không bị cạnh tranh hay khóa lẫn nhau (mỗi pod nhận một tập 100 sự kiện khác nhau).
   - Sau khi Kafka xác nhận `Ack (Committed)` thành công:
     ```sql
     UPDATE common.outbox_events 
     SET status = 'PROCESSED', processed_at = NOW() 
     WHERE id = @id;
     ```
   - Nếu Kafka lỗi: Tăng `retry_count`, ghi nhận `error_message`, áp dụng Exponential Backoff.

3. **Archiving & Retention:**
   - Để tránh phình to bảng `outbox_events`, một cleanup job chạy lúc 02:00 sáng hàng ngày sẽ di dời hoặc xóa các bản ghi có trạng thái `PROCESSED` có tuổi thọ lớn hơn 7 ngày.

## Consequences
### Positive
- **Đảm bảo toàn vẹn dữ liệu:** Không bao giờ xảy ra tình trạng mất mát sự kiện khi cơ sở dữ liệu đã commit thành công.
- **Tách rời độ khả dụng (Availability Decoupling):** Khi Kafka tạm thời downtime để bảo trì, API phục vụ khách hàng vẫn hoạt động 100% bình thường (outbox tích tụ an toàn trong PostgreSQL và sẽ tự động bắn bù khi Kafka online trở lại).

### Negative / Trade-offs
- Tăng nhẹ I/O trên PostgreSQL do thêm 1 thao tác INSERT bản ghi outbox.
- Sự kiện xuất bản có độ trễ nhỏ (thông thường từ 50ms - 200ms tùy tần suất polling).
- Người tiêu thụ (Consumer) phải chấp nhận nhận trùng thông điệp (At-least-once delivery) và phải triển khai cơ chế lọc trùng (Idempotent Consumer).
