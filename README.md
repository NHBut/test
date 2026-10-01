# Production-Ready E-Commerce Platform

Một hệ sinh thái thương mại điện tử cấp độ Enterprise được thiết kế và xây dựng theo chuẩn **Senior Software Architect**, **Modular Monolith**, tối ưu cho **High Concurrency**, **Zero-Overselling**, **Fault Tolerance**, **Transactional Outbox**, và **Horizontal Scaling**.

---

## 1. Architectural Highlights

- **Kiến trúc Modular Monolith:** Phân chia ranh giới 11 Bounded Contexts độc lập tuyệt đối (`Identity`, `Product`, `Inventory`, `Cart`, `Order`, `Payment`, `Promotion`, `Shipping`, `Review`, `Notification`, `Search`). Sẵn sàng tách thành Microservices độc lập mà không cần refactor logic nghiệp vụ.
- **PostgreSQL Schema Isolation:** Mỗi module sở hữu một schema vật lý riêng biệt (`identity.*`, `product.*`, `inventory.*`, `orders.*`, `payment.*`, `common.*`). Tuyệt đối không tạo Foreign Key vật lý xuyên schema.
- **Zero-Oversell Concurrency Control:** Áp dụng **Atomic Conditional Update** trực tiếp tại database engine kết hợp State Machine với TTL Expiry Worker và Redis Lua Script Flash Sale Token.
- **Transactional Outbox Pattern:** Loại bỏ hoàn toàn Dual-Write Problem. Mọi domain event được ghi đồng thời với dữ liệu nghiệp vụ, worker nền sử dụng `FOR UPDATE SKIP LOCKED` để publish lên Apache Kafka.
- **Idempotency by Design:** Idempotency-Key Middleware bảo vệ 100% các thao tác thay đổi tiền tệ và đơn hàng, ngăn chặn lặp request khi client retry.
- **Deep Observability:** Tích hợp W3C TraceContext xuyên suốt API -> Kafka -> Consumer -> Database, kết hợp Prometheus Metrics và Structured JSON Logging.

---

## 2. Solution Structure

```
├── backend/
│   ├── ECommerce.slnx                      # Solution XML mới (.NET 9)
│   ├── Dockerfile                          # Multi-stage production Dockerfile
│   ├── src/
│   │   ├── ECommerce.Api/                  # Web API Host, Bootstrapper & Routing
│   │   ├── ECommerce.BuildingBlocks/       # Core Domain, Outbox, Kafka, Redis, Security
│   │   └── Modules/                        # 11 Autonomous Bounded Contexts
│   │       ├── Identity/                   # JWT, Refresh Token Rotation, PBKDF2 Hasher
│   │       ├── Product/                    # Catalog, Variants, Categories, Cache Aside
│   │       ├── Inventory/                  # Atomic Reservations, Expiry Worker
│   │       ├── Cart/                       # High-speed Redis Hash Storage
│   │       ├── Order/                      # Strict State Machine, Outbox Integration
│   │       ├── Payment/                    # Asynchronous Webhook, HMAC-SHA256, Refunds
│   │       ├── Promotion/                  # Coupon rules & discount engine
│   │       ├── Shipping/                   # 3PL tracking & fulfillment
│   │       ├── Review/                     # Verified purchase rating
│   │       ├── Notification/               # Kafka consumer event notification
│   │       └── Search/                     # OpenSearch full-text & faceted search
│   └── tests/
│       ├── UnitTests/                      # Order State Machine, Coupon calculation tests
│       ├── ArchitectureTests/              # Enforcing module boundary isolation
│       └── IntegrationTests/               # Integration tests
├── infrastructure/
│   ├── docker/                             # Docker Compose full-stack & init-db.sql
│   ├── kubernetes/                         # Deployment, Service, HPA, PDB, Ingress
│   └── monitoring/                         # Prometheus configuration
├── docs/                                   # Kiến trúc & Thiết kế kỹ thuật chi tiết
│   ├── architecture.md                     # Bản thiết kế kiến trúc toàn cảnh
│   ├── implementation-plan.md              # Lộ trình 20 bước triển khai
│   ├── database.md                         # ERD, schemas & index rationale
│   ├── inventory.md                        # Concurrency & Zero-Oversell spec
│   ├── order-flow.md                       # Deterministic State Machine spec
│   ├── payment.md                          # Asynchronous webhook & HMAC security
│   ├── kafka.md                            # Topic taxonomy & consumer resilience
│   ├── redis.md                            # Caching patterns & Flash sale Lua script
│   ├── api.md                              # REST API & RFC 7807 problem details
│   ├── scaling.md                          # Horizontal scaling & Read-replica routing
│   ├── observability.md                    # OpenTelemetry, Prometheus & Loki
│   ├── deployment.md                       # Docker & Kubernetes guide
│   ├── load-testing.md                     # k6 benchmark specs
│   ├── architecture-review.md              # Trả lời 9 câu hỏi stress-test quan trọng
│   └── architecture-decisions/             # 8 Architecture Decision Records (ADRs)
│       ├── ADR-001-modular-monolith.md
│       ├── ADR-002-postgresql.md
│       ├── ADR-003-redis.md
│       ├── ADR-004-kafka.md
│       ├── ADR-005-outbox.md
│       ├── ADR-006-inventory-concurrency.md
│       ├── ADR-007-search.md
│       └── ADR-008-kubernetes.md
└── tests/load/scenarios/                   # k6 load testing scripts (flash-sale, browsing)
```

---

## 3. Quick Start & Local Setup

### 3.1. Yêu cầu môi trường
- .NET 9 SDK
- Docker & Docker Compose
- Node.js (tùy chọn cho Frontend)
- k6 (cho kiểm thử tải)

### 3.2. Khởi chạy toàn bộ hạ tầng (Postgres, Redis, Kafka, OpenSearch, MinIO, Prometheus, Grafana, Jaeger)
```bash
docker compose -f infrastructure/docker/docker-compose.yml up -d
```

### 3.3. Build & Chạy Backend API
```bash
# Build toàn bộ 16 projects
dotnet build backend/ECommerce.slnx

# Khởi chạy Web API Host
dotnet run --project backend/src/ECommerce.Api/ECommerce.Api.csproj
```
Truy cập Swagger UI tại: `http://localhost:8080/swagger`

---

## 4. Chạy Automated Tests

### 4.1. Unit Tests
Kiểm thử State Machine của Đơn hàng, tính toán Giảm giá Coupon, PBKDF2 Password Hashing:
```bash
dotnet test backend/tests/UnitTests/ECommerce.UnitTests.csproj
```

### 4.2. Architecture Tests
Kiểm thử tính toàn vẹn và cô lập ranh giới module (Zero cross-module direct reference):
```bash
dotnet test backend/tests/ArchitectureTests/ECommerce.ArchitectureTests.csproj
```

---

## 5. Chạy Load Test (k6)

### 5.1. Kịch bản Flash Sale Concurrency (1000 VU tranh mua 1 SKU)
Kiểm chứng không bán vượt tồn kho (Zero-Oversell) và Idempotency:
```bash
k6 run tests/load/scenarios/flash-sale.js
```

### 5.2. Kịch bản Duyệt Catalog
```bash
k6 run tests/load/scenarios/browsing.js
```

---

## 6. Triển khai Kubernetes (Production)

```bash
kubectl apply -f infrastructure/kubernetes/k8s-manifests.yaml
```
- **HPA:** Tự động scale từ 3 đến 30 Pods khi CPU/Memory > 70%.
- **PDB:** Luôn đảm bảo tối thiểu 2 Pods sẵn sàng khi node bảo trì.
- **Probes:** Tự động giám sát `/health/live` và `/health/ready`.
