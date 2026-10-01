# ADR-004: Apache Kafka as Event Streaming Platform, Partitioning & Consumer Resilience

## Status
Accepted

## Context
Trong hệ thống E-Commerce, các hành động như đặt hàng, thanh toán, cập nhật kho hàng, gửi email thông báo và đồng bộ dữ liệu tìm kiếm cần diễn ra theo mô hình bất đồng bộ để đảm bảo khả năng chịu tải và giảm độ trễ cho người dùng cuối.
Nếu dùng synchronous HTTP/REST giữa các module:
- Một module chậm (ví dụ cổng thanh toán hoặc dịch vụ email) sẽ gây tắc nghẽn toàn bộ luồng checkout.
- Rất khó xử lý retry khi xảy ra lỗi mạng mà không gây duplicate transaction.
- Không thể mở rộng độc lập các consumer xử lý nền.

Nếu dùng message broker dạng hàng đợi truyền thống (như RabbitMQ đơn thuần không có event replay hoặc database table làm queue):
- Khó scale thông lượng hàng chục nghìn msg/s theo luồng partition.
- Không thể replay sự kiện để re-index dữ liệu tìm kiếm hoặc phục hồi hệ thống sau sự cố.

## Decision
Chúng tôi chọn **Apache Kafka** làm Message Bus & Event Streaming Platform cho toàn bộ hệ thống E-Commerce:

1. **Topic Taxonomy & Partition Key Strategy:**
   - Các topic được phân tách theo ranh giới nghiệp vụ:
     - `order-events` (OrderCreated, OrderPaid, OrderConfirmed, OrderCancelled, OrderDelivered)
     - `payment-events` (PaymentInitiated, PaymentCompleted, PaymentFailed, RefundIssued)
     - `inventory-events` (InventoryReserved, InventoryReleased, StockDepleted)
     - `product-events` (ProductCreated, ProductUpdated, ProductDeleted)
     - `notification-events` (SendEmailRequested, SendSmsRequested)
   - **Partition Key Invariant:**
     - `order-events` sử dụng `OrderId` làm Partition Key.
     - `inventory-events` sử dụng `SkuId` làm Partition Key.
     - `product-events` sử dụng `ProductId` làm Partition Key.
     - *Lý do:* Kafka đảm bảo **Strict Ordering (FIFO)** trên cùng một partition. Tất cả các sự kiện thay đổi trạng thái của cùng một đơn hàng hoặc một SKU sẽ luôn được xử lý tuần tự bởi cùng một consumer instance, tránh race condition khi cập nhật trạng thái.

2. **Consumer Resilience & Error Handling:**
   - **At-Least-Once Delivery & Idempotent Consumer:** Kafka không đảm bảo Exactly-Once xuyên suốt các hệ thống bên ngoài. Mọi consumer bắt buộc phải là Idempotent (kiểm tra `processed_events` table hoặc hash trước khi thực thi business logic).
   - **Dead Letter Topic (DLT) Pattern:**
     - Retry tức thời: 3 lần với Exponential Backoff & Jitter.
     - Nếu tiếp tục lỗi sau 3 lần: Sự kiện được đẩy sang DLT (ví dụ: `order-events-dlt`) để không chặn offset commit của partition.
     - Đội ngũ vận hành có dashboard giám sát và API để replay sự kiện từ DLT khi bug đã được khắc phục.

3. **Offsets & Commit Strategy:**
   - Tắt `enable.auto.commit = false`. Consumer chỉ commit offset sau khi đã hoàn thành xử lý business logic và ghi nhận vào cơ sở dữ liệu.

## Consequences
### Positive
- Khả năng scale ngang độc lập: Mỗi consumer group có thể mở rộng số lượng pod bằng với số lượng partitions của topic.
- Độ bền bỉ cao: Dữ liệu được lưu trữ trên Kafka log với retention time (ví dụ: 7 ngày), cho phép replay để khôi phục hoặc phân tích lại.
- Hệ thống giải phóng tài nguyên: User nhận phản hồi ngay sau khi order được tạo, các tác vụ nặng (thông báo, xuất hóa đơn, cập nhật thống kê) chạy nền hoàn toàn.

### Negative / Trade-offs
- Chi phí hạ tầng và vận hành Kafka cluster (Zookeeper / KRaft).
- Phải thiết kế hệ thống chấp nhận tính nhất quán cuối cùng (Eventual Consistency) và độ trễ phân tán (Consumer lag).
