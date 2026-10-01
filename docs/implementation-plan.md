# Implementation Plan: E-Commerce Modular Monolith

## Plan Overview
Kế hoạch triển khai hệ thống E-commerce production-ready tuân thủ nghiêm ngặt 20 bước phát triển kiến trúc chuyên nghiệp, thực hiện từng module theo quy tắc **Zero Over-engineering**, **Correctness First**, và kiểm chứng liên tục qua Unit/Integration tests.

---

## 20-Step Implementation Roadmap

### Phase 1: Architecture, Modeling & Foundations (Steps 1 - 7)
- [x] **STEP 1: Requirements Analysis** — Phân tích chi tiết ranh giới nghiệp vụ, lưu lượng concurrency, SLA chịu lỗi và luồng tương tác giữa 11 Bounded Contexts.
- [x] **STEP 2: Architecture Blueprint & Decisions** — Hoàn thiện `docs/architecture.md` và 8 Architecture Decision Records (ADR-001 -> ADR-008).
- [ ] **STEP 3: Bounded Contexts Design** — Thiết lập Solution structure, BuildingBlocks, Contracts và Interface segregation giữa các module.
- [ ] **STEP 4: Database ERD & Schema Design** — Thiết kế chuẩn hóa relational schema PostgreSQL phân chia schema rõ ràng (`identity`, `product`, `inventory`, `orders`, `payment`, etc.), các partial index và check constraints.
- [ ] **STEP 5: API Specification & Contracts** — Định nghĩa chuẩn RESTful API, Error Model (RFC 7807), Idempotency Middleware và Versioning strategy.
- [ ] **STEP 6: Concurrency & Lock Modeling** — Đặc tả chi tiết cơ chế Atomic Conditional Updates cho Inventory, Redis Distributed Lock, và Flash Sale flow.
- [ ] **STEP 7: Event-Driven Architecture & Outbox Spec** — Đặc tả Domain Events, Kafka Topics, Partition Keys, Outbox polling worker với `FOR UPDATE SKIP LOCKED`.

### Phase 2: Core Infrastructure & Shared BuildingBlocks (Step 8)
- [ ] **STEP 8: Implement Database & BuildingBlocks**
  - Khởi tạo .NET 9 Solution (`ECommerce.sln`), dự án `ECommerce.BuildingBlocks` và `ECommerce.Api`.
  - Cấu hình Entity Framework Core với PostgreSQL, Npgsql connection pooling, migrations per schema.
  - Xây dựng Transactional Outbox pattern, UnitOfWork, Domain Event Dispatcher.
  - Implement Idempotency Middleware với bảng `idempotency_keys`.
  - Tích hợp Serilog Structured JSON Logging và CorrelationId middleware.

### Phase 3: Domain & Application Modules Implementation (Steps 9 - 14)
- [ ] **STEP 9: Identity & Product Modules**
  - **Identity:** User registration, password hashing (Argon2id/BCrypt), JWT token generation, refresh token rotation, Role & Permission claims.
  - **Product & Category:** Aggregate roots, SKU variants, attributes, category hierarchies, pricing logic.
- [ ] **STEP 10: Inventory Module with Concurrency Control**
  - Quản lý Stock theo SKU.
  - Triển khai **Reserve Inventory** (Atomic conditional decrement: `available - qty >= 0`), **Confirm Reservation**, **Release Reservation**, và **Background Expiry Worker**.
  - Kiểm tra affected rows để trả mã lỗi tức thì mà không bị race conditions.
- [ ] **STEP 11: Cart & Redis Integration**
  - Triển khai Cart Domain với Redis High-Performance storage.
  - Cache Aside pattern cho Product Catalog, giải quyết Cache Stampede (Probabilistic Early Expiration hoặc Mutex Lock) và Cache Penetration (Null object caching).
- [ ] **STEP 12: Order Module & State Machine**
  - Aggregate Root `Order` với State Machine chuẩn: `PENDING_PAYMENT` -> `PAID` -> `CONFIRMED` -> `PROCESSING` -> `SHIPPING` -> `DELIVERED` / `CANCELLED` / `REFUNDED`.
  - Transactional Outbox ghi nhận `OrderCreatedDomainEvent`.
- [ ] **STEP 13: Apache Kafka Integration & Outbox Worker**
  - Kafka Producer với resilience và batching.
  - Kafka Consumer framework hỗ trợ: Idempotent Consumer (Deduplication record tracking), Dead Letter Topic (DLT), Retry backoff.
  - Background Worker publish Outbox Events lên Kafka topic `order-events`, `inventory-events`.
- [ ] **STEP 14: Payment Module & Webhooks**
  - Asynchronous Payment processing: Giả lập Payment Gateway integration.
  - Webhook endpoint xử lý Idempotency, Signature verification, Out-of-order webhook detection.
  - Compensation transactions khi thanh toán thất bại (tự động nhả inventory reservation).
- [ ] **STEP 15: Search Module (OpenSearch / Elasticsearch)**
  - Tích hợp Search Indexer qua Kafka consumer (nghe `ProductCreated`/`ProductUpdated` và sync OpenSearch).
  - OpenSearch Query Builder cho full-text search, faceted category navigation, sorting.

### Phase 4: Observability, DevOps & Frontend (Steps 16 - 17)
- [ ] **STEP 16: Observability Suite**
  - Cấu hình OpenTelemetry Tracing (W3C standard) cho ASP.NET Core, EF Core, Redis, Kafka.
  - Prometheus Metrics endpoint và Grafana Dashboards.
- [ ] **STEP 17: Containerization & Orchestration**
  - Multi-stage Dockerfile tối ưu (non-root user, minimal image size, healthchecks).
  - `docker-compose.yml` hoàn chỉnh tích hợp: Postgres, Redis, Kafka, OpenSearch, MinIO, Prometheus, Grafana, Jaeger/Tempo.
  - Kubernetes Manifests: Deployments, Services, ConfigMaps, Secrets, Ingress, HPA, PodDisruptionBudget, Liveness/Readiness probes.

### Phase 5: Verification, Load Testing & Reviews (Steps 18 - 20)
- [ ] **STEP 18: Testing & Verification**
  - Unit Tests: State Machine transitions, Inventory calculation logic, Domain invariants.
  - Integration Tests: Testcontainers cho PostgreSQL và Redis, kiểm thử concurrency checkout (nhiều threads tranh chấp 1 SKU).
  - Load Test (k6): Các kịch bản Browsing, Search, Add to cart, Concurrent Checkout, Flash sale.
- [ ] **STEP 19: Security & Resilience Audit**
  - Rà soát OWASP Top 10: SQL injection, broken authentication, IDOR, sensitive data exposure, rate limiting.
  - Rà soát failure modes: PostgreSQL failover, Redis crash fallback, Kafka lag handling.
- [ ] **STEP 20: Comprehensive Architecture Review**
  - Tạo `docs/architecture-review.md` trả lời cặn kẽ tất cả câu hỏi giả định (10x, 100x traffic, Redis dies, Kafka dies, Read replica lag, 100k users / 100 stock).

---

## Definition of Done (DoD) Checklist
1. Toàn bộ C# code compile sạch (`0 Errors`, `0 Warnings` nghiêm trọng).
2. Toàn bộ Unit và Integration tests pass.
3. Docker Compose khởi chạy thành công toàn bộ stack, healthchecks báo green.
4. Database migrations chạy trơn tru per-schema.
5. Kiểm thử concurrency không xảy ra oversell hay deadlock.
6. Idempotency Middleware ngăn chặn 100% duplicate submit.
7. Outbox Worker đảm bảo at-least-once delivery tới Kafka.
8. Tài liệu kỹ thuật, API docs, ADRs và Architecture Review hoàn thiện đầy đủ.
