# ADR-001: Architectural Style - Modular Monolith with Clear Domain Boundaries

## Status
Accepted

## Context
Xây dựng một hệ thống E-Commerce quy mô lớn có yêu cầu chịu tải cao, khả năng mở rộng ngang, độ trễ thấp và tính nhất quán dữ liệu cao.
Việc lựa chọn ngay kiến trúc Microservices ngay từ giai đoạn ban đầu thường dẫn đến:
1. **Distributed Complexity Overkill:** Phát sinh các bài toán cực kỳ phức tạp về distributed transactions (2PC hoặc Saga phức tạp), network partition, RPC latency, data fragmentation và khó debug.
2. **Operational Overhead:** Cần quản lý hàng chục CI/CD pipelines, container orchestration, service discovery, mesh network và phân mảnh monitoring khi domain model chưa thực sự ổn định.
3. **Mối nguy "Distributed Monolith":** Do các ranh giới nghiệp vụ e-commerce (như Product, Inventory, Cart, Order, Promotion) có tương tác chặt chẽ, việc phân tách sớm và sai lầm sẽ biến hệ thống thành một distributed monolith mang mọi nhược điểm của cả hai kiến trúc.

Ngược lại, một Monolith truyền thống không có ranh giới rõ ràng sẽ nhanh chóng biến thành "Big Ball of Mud", nơi code của Module Order gọi trực tiếp DbContext của Inventory, tạo ra coupling chặt chẽ khiến hệ thống không thể scale hay chia tách sau này.

## Decision
Chúng tôi quyết định áp dụng kiến trúc **Modular Monolith** với các nguyên tắc nghiêm ngặt:

1. **Autonomous Bounded Contexts:**
   Hệ thống được chia thành 11 Bounded Contexts độc lập: Identity, Product, Inventory, Cart, Order, Payment, Promotion, Shipping, Review, Notification, Search.
2. **Physical Boundary Isolation:**
   Mỗi module là một tập hợp các project/namespace độc lập (Domain, Application, Infrastructure, API). Không module nào được phép tham chiếu trực tiếp project Infrastructure hoặc DbContext của module khác.
3. **Dedicated Database Schemas:**
   Tất cả các module chia sẻ một PostgreSQL cluster nhưng mỗi module sở hữu một PostgreSQL Schema riêng biệt (ví dụ: `identity`, `product`, `inventory`, `orders`). Không có Physical Foreign Key giữa các schema khác nhau.
4. **Communication Invariants:**
   - **Internal Synchronous Queries:** Sử dụng In-Memory Query Handlers (MediatR/Contracts) với data models DTO tối giản.
   - **Cross-Module Mutating Commands:** Không được gọi Command của module khác trực tiếp. Thay vào đó, sử dụng **Domain Events** xuất bản thông qua **Apache Kafka** để đạt được Eventual Consistency.
5. **Microservices Extraction Readiness:**
   Khi một module (ví dụ: Inventory hoặc Search) chịu tải đột biến vượt trội, module đó có thể được đóng gói thành một Docker image độc lập, trỏ vào schema database riêng và triển khai như một Microservice riêng biệt mà không cần viết lại logic nghiệp vụ.

## Consequences
### Positive
- **Độ trễ thấp & Thông lượng cao:** Giao tiếp nội bộ in-process không tốn chi phí network serialization/deserialization.
- **Dễ dàng phát triển & Test:** Developer có thể chạy và debug toàn bộ hệ thống trên một máy tính cá nhân mà không cần cài đặt một mesh khổng lồ.
- **Consistency cao:** Dễ dàng kiểm soát transaction boundary trong từng module.
- **Lộ trình tiến hóa rõ ràng:** Giữ trọn vẹn ranh giới DDD để sẵn sàng tách dịch vụ khi traffic vượt ngưỡng xử lý của Monolith cluster.

### Negative / Trade-offs
- Yêu cầu kỷ luật cao: Đội ngũ phát triển phải tuân thủ nghiêm ngặt ArchUnit/Architecture Tests để ngăn chặn việc dev vô tình gọi chéo DbContext hoặc model giữa các module.
- Tất cả các module phải triển khai cùng một phiên bản runtime khi ở dạng Monolith deployment.
