# Deployment Architecture & Operations Guide

## 1. Container Strategy & Multi-Stage Dockerfile
Để đảm bảo an toàn và kích thước image tối thiểu khi vận hành trong production:
- **Build Stage:** Sử dụng .NET SDK image đầy đủ để compile và chạy tests.
- **Runtime Stage:** Sử dụng image `mcr.microsoft.com/dotnet/aspnet:9.0-alpine` hoặc `chiseled` tối giản (< 120MB).
- **Security:**
  - Tuyệt đối không chạy container dưới quyền `root` (sử dụng người dùng `app` với UID 1654).
  - Không hardcode secret hoặc credentials vào image.
  - Image filesystem ở chế độ `read-only`, chỉ cấp quyền ghi vào thư mục `/tmp` tạm thời nếu cần thiết.

---

## 2. Local Infrastructure Stack (Docker Compose)

Môi trường local development và integration test được khởi tạo đầy đủ bằng một lệnh duy nhất:
```bash
docker compose -f infrastructure/docker/docker-compose.yml up -d
```

Các dịch vụ bao gồm:
1. `postgres`: PostgreSQL 16 Alpine (Port 5432)
2. `redis`: Redis 7 Alpine (Port 6379)
3. `kafka`: Apache Kafka (KRaft mode - không cần Zookeeper) (Port 9092)
4. `opensearch`: OpenSearch 2.12 (Port 9200)
5. `minio`: MinIO Object Storage (Port 9000/9001)
6. `prometheus`: Thu thập metrics (Port 9090)
7. `grafana`: Hiển thị dashboard (Port 3000)
8. `jaeger`: Thu thập và hiển thị Tracing OTel (Port 16686)

---

## 3. Kubernetes Orchestration Architecture

### 3.1. Workload Separation
- **Web API Deployments (`ecommerce-api`):** Xử lý traffic đồng bộ từ khách hàng, kết nối Ingress.
- **Background Outbox Workers (`ecommerce-outbox-worker`):** Quét bảng outbox và publish lên Kafka.
- **Async Consumer Workers (`ecommerce-consumers`):** Tiêu thụ sự kiện từ Kafka và thực thi nghiệp vụ nền.

### 3.2. Autoscaling & High Availability
- **Horizontal Pod Autoscaler (HPA):**
  - Metric: CPU > 70% hoặc Memory > 70%.
  - Min Replicas: 3 | Max Replicas: 30.
- **PodDisruptionBudget (PDB):** Đảm bảo luôn duy trì tối thiểu 2 Pods hoạt động trong bất kỳ đợt bảo trì nâng cấp node nào.
- **Zero-Downtime Rolling Update:**
  ```yaml
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 25%
      maxUnavailable: 0
  ```

### 3.3. Probes & Healthchecks
- `/health/live`: Liveness Probe - Kiểm tra process có bị deadlock/hang hay không.
- `/health/ready`: Readiness Probe - Kiểm tra kết nối tới DB, Redis, Kafka trước khi phân bổ traffic từ Service.
