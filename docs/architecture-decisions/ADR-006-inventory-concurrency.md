# ADR-006: Inventory Concurrency Control, Reservation Lifecycle & Flash Sale Protection

## Status
Accepted

## Context
Quản lý tồn kho là thành phần có rủi ro kinh doanh cao nhất trong E-Commerce:
1. **Overselling (Bán vượt tồn kho):** Khi số lượng hàng trong kho chỉ còn 1 nhưng có hàng trăm người bấm mua đồng thời trong cùng một phần nghìn giây. Nếu không có cơ chế khóa/kiểm soát đồng thời, tồn kho có thể bị âm (`available_quantity < 0`), dẫn đến việc hủy đơn hàng loạt, phạt từ sàn thương mại điện tử và mất uy tín nghiêm trọng.
2. **Deadlocks:** Nếu nhiều transaction cập nhật nhiều SKU theo thứ tự khác nhau (User 1 mua SKU A rồi SKU B; User 2 mua SKU B rồi SKU A), cơ sở dữ liệu sẽ phát sinh Deadlock và ngắt kết nối client.
3. **Ghost Inventory (Tồn kho ma):** Khách hàng đặt hàng nhưng không chịu thanh toán, hàng bị giữ mãi mãi trong trạng thái "Reserved" khiến người khác không mua được.
4. **Flash Sale Spike:** 100,000 người dùng cùng tranh mua 100 sản phẩm trong 3 giây. Nếu đẩy thẳng vào RDBMS, hàng chục nghìn connection sẽ đánh sập PostgreSQL instance.

## Decision
Chúng tôi thiết kế chiến lược quản lý tồn kho đa tầng như sau:

### 1. Standard Concurrency: Single SQL Atomic Conditional Update
Tuyệt đối cấm anti-pattern:
`SELECT stock -> app code check -> UPDATE`
Thay vào đó, thực hiện **Atomic Conditional Decrement** trực tiếp tại database engine:
```sql
UPDATE inventory.product_inventories
SET available_quantity = available_quantity - @quantity,
    reserved_quantity = reserved_quantity + @quantity,
    updated_at = NOW(),
    version = version + 1
WHERE sku_id = @skuId 
  AND available_quantity >= @quantity;
```
- Lệnh này tận dụng Row-Level Locking tự nhiên của PostgreSQL.
- Kiểm tra kết quả thực thi:
  - Nếu `RowsAffected == 1`: Thành công. Ghi nhận bản ghi vào `inventory.inventory_reservations` với `status = 'PENDING'` và `expires_at = NOW() + INTERVAL '15 minutes'`.
  - Nếu `RowsAffected == 0`: Trả mã lỗi `OUT_OF_STOCK` ngay lập tức, rollback toàn bộ transaction trong mili-giây mà không gây ảnh hưởng đến hàng đợi.

### 2. Multi-Item Deadlock Prevention
Khi một giỏ hàng có nhiều SKU (ví dụ: mua đồng thời SKU-10, SKU-02, SKU-45):
- Ứng dụng **bắt buộc sắp xếp danh sách SKU theo thứ tự tăng dần (Alphabetical / ID sorting)** trước khi thực thi truy vấn kho.
- Nhờ luôn cập nhật các dòng theo thứ tự đồng nhất (SKU-02 -> SKU-10 -> SKU-45), nguy cơ Deadlock giữa các transaction đồng thời giảm về 0%.

### 3. Reservation Lifecycle & Auto-Release Expiry Worker
Vòng đời của Reservation:
- `PENDING`: Đang chờ thanh toán (TTL 15 phút).
- `CONFIRMED`: Thanh toán thành công -> chuyển `reserved_quantity = reserved_quantity - @qty` và ghi nhận hàng đã xuất kho.
- `CANCELLED` / `EXPIRED`: Khách hủy hoặc quá 15 phút chưa thanh toán.
- **Worker giải phóng:** Background worker chạy mỗi 30 giây:
  ```sql
  -- Tìm các reservation quá hạn
  SELECT id, sku_id, quantity 
  FROM inventory.inventory_reservations 
  WHERE status = 'PENDING' AND expires_at < NOW() 
  LIMIT 100 FOR UPDATE SKIP LOCKED;
  
  -- Hoàn lại kho
  UPDATE inventory.product_inventories 
  SET available_quantity = available_quantity + @quantity,
      reserved_quantity = reserved_quantity - @quantity
  WHERE sku_id = @skuId;
  ```

### 4. Flash Sale Tier: Redis Atomic Token Pre-Allocation
Với các sự kiện siêu khuyến mãi quy mô cực lớn (100,000 CCU / 100 sản phẩm):
- Tồn kho của Flash SKU được nạp trước vào Redis: `SET flash:sku:{skuId}:stock 100`.
- Chặn 99.9% request tại tầng API bằng Redis Lua Script (Atomic check & decrement):
  ```lua
  local stock = redis.call('get', KEYS[1])
  if not stock or tonumber(stock) < tonumber(ARGV[1]) then
      return 0
  else
      redis.call('decrby', KEYS[1], ARGV[1])
      return 1
  end
  ```
- Chỉ 100 request đầu tiên nhận `Result == 1` được cấp Token để tiếp tục đẩy vào Kafka queue xử lý tạo đơn và trừ DB thực tế. 99,900 request còn lại nhận phản hồi `409 Conflict (Hết hàng)` tức thì ở tầng Redis (< 2ms) mà cơ sở dữ liệu PostgreSQL hoàn toàn không bị ảnh hưởng.

## Consequences
### Positive
- **100% Zero Overselling:** Không bao giờ bán vượt quá số lượng trong kho.
- **Không xảy ra Deadlock:** Nhờ quy tắc sắp xếp khóa trước khi trừ kho.
- **Tự động giải phóng tồn kho:** Không sợ khách hàng chiếm giữ hàng rồi bỏ quên.
- **Khả năng chịu tải cực cao:** Ngăn chặn hoàn toàn hiện tượng DDoS cơ sở dữ liệu khi có Flash sale.

### Negative / Trade-offs
- Logic vòng đời reservation phức tạp hơn so với trừ kho trực tiếp.
- Mô hình Flash Sale với Redis có tính nhất quán cuối cùng: Cần có worker đối soát định kỳ nếu consumer xử lý tạo đơn bị lỗi ngoài ý muốn.
