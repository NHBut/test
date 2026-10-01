# ADR-007: OpenSearch for High-Scale Catalog Search and Event-Driven Indexing

## Status
Accepted

## Context
Trong thương mại điện tử, hành vi người dùng chủ yếu là tìm kiếm và duyệt sản phẩm (Product Discovery).
Các truy vấn tìm kiếm rất đa dạng:
- Tìm theo từ khóa không dấu, có dấu, gõ sai chính tả (fuzzy search).
- Tìm kiếm theo cụm thuộc tính đa chiều (Faceted navigation: Màu sắc, Size, Thương hiệu, Khoảng giá, Đánh giá sao).
- Sắp xếp động theo độ phổ biến, bán chạy, giá tăng/giảm.

Nếu thực hiện tìm kiếm bằng PostgreSQL:
- Lệnh `LIKE '%keyword%'` hoặc `ILIKE` yêu cầu Full Table Scan, làm tê liệt CPU database khi catalog có hàng triệu sản phẩm và hàng chục nghìn người tìm kiếm đồng thời.
- Ngay cả PostgreSQL Full Text Search (`tsvector`, GIN index) cũng gặp giới hạn lớn về bộ nhớ khi xử lý aggregation đa chiều (faceting aggregations) và không scale ngang độc lập được với transactional workload.

## Decision
Chúng tôi quyết định tách riêng công cụ tìm kiếm và sử dụng **OpenSearch** (hoặc Elasticsearch tương thích) cho Product Search:

1. **Role of OpenSearch:**
   - Đóng vai trò là Read-Only Search Index chuyên biệt cho người dùng cuối.
   - Hỗ trợ Full-text search, N-gram tokenizers, Auto-complete suggestions, và Multi-facet Aggregations cực nhanh (< 20ms).
   - PostgreSQL vẫn luôn là Single Source of Truth cho dữ liệu sản phẩm và giá gốc.

2. **Asynchronous CDC / Event-Driven Sync Pipeline:**
   Tuyệt đối **không** gọi OpenSearch API đồng bộ trong quá trình lưu sản phẩm ở admin API (tránh việc OpenSearch lag làm chậm quá trình tạo/sửa sản phẩm):
   ```
   Product DB (PostgreSQL) 
       -> Outbox Table 
       -> Kafka (product-events topic) 
       -> Search Indexer Worker 
       -> OpenSearch Cluster
   ```
   - Khi có `ProductCreated`, `ProductUpdated`, `ProductDeleted`: Worker tiêu thụ message từ Kafka và thực thi Bulk API lên OpenSearch.
   - Nếu OpenSearch tạm thời gián đoạn: Kafka retry với backoff, dữ liệu index không bao giờ bị mất mát.

3. **Re-indexing Strategy:**
   - Thiết kế Index Alias (`products_active` trỏ tới `products_v1`).
   - Khi cần thay đổi cấu trúc mapping, thêm analyzer hoặc re-index toàn bộ dữ liệu:
     1. Tạo index mới `products_v2`.
     2. Chạy background bulk-load job từ PostgreSQL sang `products_v2`.
     3. Đổi alias `products_active` sang `products_v2` với zero-downtime.
     4. Xóa index cũ `products_v1`.

## Consequences
### Positive
- Phản hồi tìm kiếm siêu tốc độ, giảm 100% gánh nặng full-text search khỏi PostgreSQL cluster.
- Hỗ trợ các tính năng tìm kiếm cao cấp: autocomplete, spellcheck, facet filter phong phú.
- Tự do scale ngang OpenSearch data nodes dựa theo lưu lượng search mà không cần nâng cấp server DB chính.

### Negative / Trade-offs
- Chấp nhận tính nhất quán cuối cùng (Eventual Consistency): Sản phẩm sau khi tạo/sửa ở CMS có thể mất 200ms - 1s mới xuất hiện trên kết quả tìm kiếm OpenSearch.
- Tăng chi phí phần cứng và công tác giám sát hệ thống (OpenSearch JVM heap, disk watermark).
