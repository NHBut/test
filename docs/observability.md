# Observability Architecture: OpenTelemetry, Prometheus, Grafana & Loki

## 1. Architectural Strategy
Hệ thống tuân thủ mô hình 3 trụ cột của Observability (Traces, Metrics, Logs), chuẩn hóa 100% theo tiêu chuẩn mở **OpenTelemetry (OTel)** và W3C TraceContext.

---

## 2. Distributed Tracing Flow (W3C TraceContext)

Mọi request từ khi chạm vào API Gateway đều được gắn một `traceparent` header theo chuẩn W3C:
`00-{trace_id}-{parent_span_id}-{trace_flags}`

```
Client Request
    │
    ▼ [HTTP Span: POST /api/v1/orders]
OrderController
    │
    ▼ [DB Span: Npgsql ExecuteNonQuery - Insert Order]
PostgreSQL
    │
    ▼ [Producer Span: Kafka ProduceAsync - order-events] (Headers injected with traceparent)
Apache Kafka
    │
    ▼ [Consumer Span: Kafka ConsumeAsync] (Trace context extracted from message headers)
Fulfillment Worker
    │
    ▼ [DB Span: Update Inventory Reservation]
PostgreSQL
```
Nhờ cơ chế truyền context qua Kafka Record Headers, toàn bộ chuỗi hành động từ lúc user click thanh toán trên web cho đến khi worker xử lý xong dưới nền đều được hiển thị thành **1 Trace duy nhất** trên Jaeger / Grafana Tempo.

---

## 3. Core Business & System Metrics Catalog (Prometheus)

Hệ thống xuất bản metrics tại endpoint `/metrics`:

### 3.1. HTTP & API Performance
- `ecommerce_http_requests_total{method, endpoint, status_code}`: Tổng số lượng request.
- `ecommerce_http_request_duration_seconds_bucket{method, endpoint, le}`: Histogram độ trễ (P50, P95, P99).
- `ecommerce_http_active_requests`: Số lượng request đang được xử lý đồng thời.

### 3.2. Database & Cache Metrics
- `ecommerce_db_connection_pool_active`: Số lượng kết nối DB đang hoạt động.
- `ecommerce_db_query_duration_seconds`: Thời gian thực thi truy vấn PostgreSQL.
- `ecommerce_redis_cache_hits_total{module}`: Số lần trúng cache.
- `ecommerce_redis_cache_misses_total{module}`: Số lần trượt cache (đo lường Cache Hit Ratio: $\frac{\text{Hits}}{\text{Hits} + \text{Misses}}$).

### 3.3. Critical Business Metrics
- `ecommerce_orders_created_total{status}`: Tốc độ tạo đơn hàng thành công/thất bại.
- `ecommerce_inventory_reservations_failed_total{reason}`: Số lần trừ kho thất bại (Oversell guard trigger).
- `ecommerce_payment_transactions_total{gateway, status}`: Tỷ lệ thanh toán thành công theo từng cổng.
- `ecommerce_kafka_consumer_lag{topic, partition, consumergroup}`: Độ trễ tiêu thụ message trên Kafka.

---

## 4. Structured JSON Logging Standards (Serilog & Loki)

Mọi log đều được serialize dưới định dạng JSON với các metadata trường bắt buộc:

```json
{
  "Timestamp": "2026-10-01T07:45:00.1234567Z",
  "Level": "Information",
  "MessageTemplate": "Order {OrderId} successfully placed for Customer {CustomerId} with Total {TotalAmount}",
  "Properties": {
    "OrderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "CustomerId": "7ca85f64-5717-4562-b3fc-2c963f66afa7",
    "TotalAmount": 2500000.0,
    "Module": "Order",
    "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
    "SpanId": "00f067aa0ba902b7",
    "CorrelationId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "Environment": "Production"
  }
}
```
Grafana Loki thu thập log này và cho phép tìm kiếm chéo theo `TraceId` hoặc `CorrelationId` trong chưa đầy 1 giây.
