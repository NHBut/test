# ADR-008: Kubernetes Deployment, Horizontal Pod Autoscaling (HPA) & Zero-Downtime Releases

## Status
Accepted

## Context
Hệ thống E-Commerce thường xuyên chứng kiến sự biến động lưu lượng rất lớn:
- Ngày bình thường: 1,000 - 3,000 RPS.
- Sự kiện Mega Campaign (11.11, 12.12, Black Friday): Có thể tăng vọt lên 50,000 - 100,000 RPS chỉ trong vài phút.
Nếu quản lý triển khai thủ công hoặc fixed server:
- Gây lãng phí tài nguyên máy chủ khủng khiếp vào ban đêm và các ngày thấp điểm.
- Không kịp phản ứng khi traffic tăng đột biến, gây nghẽn hàng đợi và sập hệ thống (OOMKilled, CPU Throttling).
- Triển khai phiên bản mới có nguy cơ gây gián đoạn dịch vụ (downtime).

## Decision
Chúng tôi quyết định chọn **Kubernetes (K8s)** làm nền tảng điều phối container sản xuất với các thiết kế chuẩn mực:

1. **Stateless Backend Application:**
   - Ứng dụng ASP.NET Core không lưu bất kỳ session hoặc in-memory state quan trọng nào trên Pod.
   - Toàn bộ state được ủy thác cho PostgreSQL (dữ liệu bền vững) và Redis (dữ liệu tạm / giỏ hàng).
   - Bất kỳ pod nào cũng có thể nhận và xử lý request của bất kỳ người dùng nào mà không phụ thuộc Sticky Sessions.

2. **Elastic Autoscaling (HPA - Horizontal Pod Autoscaler):**
   - Không cấu hình cứng `replicas`. Thiết lập HPA theo CPU & Memory:
     - `minReplicas: 3` (Đảm bảo High Availability và Zero-Downtime khi rolling update).
     - `maxReplicas: 30` (hoặc cao hơn theo hạn mức cluster).
     - Target: `averageUtilization: 70%` cho CPU và Memory.
   - Hỗ trợ KEDA (Kubernetes Event-driven Autoscaling) cho Outbox Worker dựa trên chỉ số Kafka Consumer Lag (nếu lag > 5000 messages thì tự động tăng pod worker).

3. **High Availability & Zero-Downtime Deployments:**
   - **RollingUpdate Strategy:** `maxSurge: 25%`, `maxUnavailable: 0` (Luôn duy trì đủ 100% capacity trước khi tắt pod cũ).
   - **PodDisruptionBudget (PDB):** Thiết lập `minAvailable: 2` để ngăn ngừa kịch bản node drain làm sập dịch vụ.
   - **Probes:**
     - `livenessProbe`: Kiểm tra HTTP ping `/health/live` (phát hiện deadlock hoặc hung process để restart pod).
     - `readinessProbe`: Kiểm tra kết nối DB, Redis, Kafka `/health/ready` (chỉ cho pod nhận traffic từ Ingress khi toàn bộ dependency đã sẵn sàng).
     - `startupProbe`: Cho phép thời gian khởi tạo cấu hình ban đầu mà không bị liveness probe kill sớm.

4. **Resource Constraints & Security Context:**
   - Bắt buộc cấu hình đầy đủ `requests` và `limits` cho từng container để tránh noisy neighbor.
   - Chạy container dưới người dùng non-root (`runAsNonRoot: true`, `readOnlyRootFilesystem: true`, `allowPrivilegeEscalation: false`).
   - Quản lý nhạy cảm thông qua Kubernetes Secrets / External Secrets Operator tích hợp AWS Secrets Manager hoặc HashiCorp Vault.

## Consequences
### Positive
- Tự động thích ứng linh hoạt theo biến động thực tế của thị trường mà không cần can thiệp thủ công.
- Tối ưu hóa chi phí điện toán đám mây (Cloud Infrastructure Costs).
- Triển khai tính năng mới với 0 giây gián đoạn (Zero-Downtime Rolling Updates).

### Negative / Trade-offs
- Độ phức tạp vận hành Kubernetes (networking, CNI, ingress controller, certificate management).
- Đòi hỏi quy trình CI/CD hoàn chỉnh (GitOps với ArgoCD / Helm).
