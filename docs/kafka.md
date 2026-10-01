# Apache Kafka Architecture: Topics, Partitioning & Consumer Resilience

## 1. Topic Taxonomy & Partitioning Strategy

Để đảm bảo hiệu năng scale ngang và trật tự sự kiện nghiêm ngặt theo thực thể, các topic được phân loại như sau:

| Topic Name | Default Partitions | Key Strategy | Mục đích & Sự kiện chính |
| :--- | :--- | :--- | :--- |
| `order-events` | 16 | `OrderId` (UUID string) | `OrderCreated`, `OrderPaid`, `OrderConfirmed`, `OrderCancelled`, `OrderDelivered` |
| `payment-events` | 16 | `OrderId` / `PaymentId` | `PaymentInitiated`, `PaymentCompleted`, `PaymentFailed`, `RefundCompleted` |
| `inventory-events`| 16 | `SkuId` (UUID string) | `InventoryReserved`, `InventoryReleased`, `StockDepleted` |
| `product-events` | 8 | `ProductId` (UUID string) | `ProductCreated`, `ProductUpdated`, `ProductDeleted` (Đồng bộ Search & Cache) |
| `notification-events` | 8 | `UserId` (UUID string) | `SendWelcomeEmail`, `SendOrderReceipt`, `SendShippingUpdate` |

### Vì sao Partition Key theo ID là bắt buộc?
Kafka chỉ đảm bảo thứ tự (Strict Ordering - FIFO) **trong phạm vi một Partition duy nhất**. Bằng cách băm (hashing) `OrderId` làm partition key:
- Tất cả các sự kiện của cùng Đơn hàng #123 (`OrderCreated` -> `OrderPaid` -> `OrderShipped`) đều chắc chắn rơi vào cùng một partition.
- Cùng một Consumer Instance trong Consumer Group sẽ tiêu thụ các sự kiện này theo đúng trình tự thời gian sinh ra, không bao giờ có chuyện `OrderShipped` bị xử lý trước `OrderCreated`.

---

## 2. Consumer Resilience: Retries & Dead Letter Topics (DLT)

Không được phép để một message lỗi làm tắc nghẽn toàn bộ partition (Head-of-Line Blocking). Mô hình xử lý lỗi đa tầng:

```mermaid
flowchart TD
    KafkaTopic["Kafka Topic: order-events"] --> Consumer["Consumer Instance"]
    Consumer --> BusinessLogic{"Thực thi Business Logic"}
    
    BusinessLogic -->|Thành công| Commit["Commit Offset"]
    BusinessLogic -->|Lỗi tạm thời (Network/DB)| RetryCheck{"Số lần retry < 3?"}
    
    RetryCheck -->|Có| Backoff["Chờ Exponential Backoff + Jitter"] --> BusinessLogic
    RetryCheck -->|Không (Đã thử 3 lần)| PublishDLT["Gửi sang Dead Letter Topic: order-events-dlt"]
    PublishDLT --> Alert["Bắn Alert sang Prometheus/Grafana"]
    PublishDLT --> Commit
```

### Dead Letter Topic Structure:
- Mỗi topic nghiệp vụ có một DLT tương ứng:
  - `order-events` -> `order-events-dlt`
  - `payment-events` -> `payment-events-dlt`
- Header của message trong DLT lưu trữ:
  - `x-original-topic`: Tên topic gốc
  - `x-exception-message`: Chi tiết lỗi phát sinh
  - `x-exception-stacktrace`: Stack trace lỗi
  - `x-retry-count`: Số lần đã retry

---

## 3. Idempotent Consumer Pattern (Lọc trùng thông điệp)

Kafka cam kết **At-least-once delivery** (có thể gửi lặp lại nếu consumer bị crash trước khi kịp commit offset). Do đó, consumer bắt buộc phải tự bảo vệ:

```csharp
public async Task HandleAsync(KafkaEventRecord record)
{
    var eventId = record.Headers["message-id"];
    
    // Kiểm tra xem message đã từng được xử lý thành công chưa
    var alreadyProcessed = await dbContext.ProcessedEvents
        .AnyAsync(x => x.EventId == eventId);

    if (alreadyProcessed)
    {
        logger.LogWarning("Duplicate event detected {EventId}, skipping.", eventId);
        return; // Bỏ qua an toàn, không xử lý lại
    }

    // Thực thi transaction nghiệp vụ kèm ghi nhận event đã xử lý
    using var tx = await dbContext.Database.BeginTransactionAsync();
    
    await ProcessBusinessLogicAsync(record);
    
    dbContext.ProcessedEvents.Add(new ProcessedEvent 
    { 
        EventId = eventId, 
        ConsumerGroup = "order-fulfillment-group",
        ProcessedAt = DateTime.UtcNow 
    });
    
    await dbContext.SaveChangesAsync();
    await tx.CommitAsync();
}
```
Nhờ cơ chế này, ngay cả khi Kafka gửi lại cùng một event 10 lần, hệ thống chỉ thực thi đúng 1 lần duy nhất.
