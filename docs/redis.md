# Redis Architectural Guidelines & High-Concurrency Patterns

## 1. Key Naming Standards & TTL Policy

Tất cả các Redis key bắt buộc phải tuân thủ định dạng chuẩn hóa, phân tách bởi dấu `:`:
`{service_or_module}:{entity_type}:{identifier}:{optional_sub_attribute}`

| Key Pattern | Cấu trúc dữ liệu | Mục đích | TTL mặc định |
| :--- | :--- | :--- | :--- |
| `product:id:{productId}` | String (JSON) | Cache chi tiết sản phẩm | 1 giờ + Jitter ngẫu nhiên (1-10 phút) |
| `product:sku:{skuCode}` | String (JSON) | Cache biến thể sản phẩm | 1 giờ + Jitter ngẫu nhiên |
| `category:tree` | String (JSON) | Cây danh mục toàn sàn | 24 giờ |
| `cart:user:{userId}` | Hash | Giỏ hàng người dùng (`field = skuId`, `value = qty`) | 30 ngày (Gia hạn khi có tương tác) |
| `ratelimit:{ip}:{endpoint}` | Sorted Set | Sliding window rate limiting | 1 phút |
| `lock:inventory:{skuId}` | String | Distributed Lock (Redlock) | 5 giây (Auto-release) |
| `flash:sku:{skuId}:stock` | Integer (String) | Tồn kho token flash sale | Thời gian diễn ra chiến dịch |

> **TTL Jitter Rule:** Không bao giờ set TTL cố định cho hàng nghìn sản phẩm cùng lúc (ví dụ đúng 3600s). Luôn cộng thêm giá trị ngẫu nhiên `TTL = BaseTTL + Random(60, 300)` để ngăn chặn việc hàng loạt key cùng hết hạn tại một thời điểm.

---

## 2. Cache Stampede & Penetration Protection

### 2.1. Cache Aside with Mutex Lock (Thundering Herd Solution)
Khi cache miss trên một hot product, chỉ duy nhất 1 thread được phép query database:

```csharp
public async Task<ProductDto?> GetProductAsync(Guid productId)
{
    var cacheKey = $"product:id:{productId}";
    var cached = await redis.GetStringAsync(cacheKey);
    if (cached != null)
    {
        if (cached == "__NULL__") return null; // Cache penetration guard
        return JsonSerializer.Deserialize<ProductDto>(cached);
    }

    var lockKey = $"lock:{cacheKey}";
    var acquired = await redis.SetStringAsync(lockKey, "1", TimeSpan.FromSeconds(3), When.NotExists);

    if (acquired)
    {
        try
        {
            var product = await dbContext.Products.FindAsync(productId);
            if (product == null)
            {
                // Ngăn chặn Cache Penetration: Lưu placeholder rỗng với TTL ngắn 2 phút
                await redis.SetStringAsync(cacheKey, "__NULL__", TimeSpan.FromMinutes(2));
                return null;
            }

            var dto = mapper.Map<ProductDto>(product);
            var jitter = TimeSpan.FromMinutes(new Random().Next(1, 10));
            await redis.SetStringAsync(cacheKey, JsonSerializer.Serialize(dto), TimeSpan.FromHours(1) + jitter);
            return dto;
        }
        finally
        {
            await redis.KeyDeleteAsync(lockKey);
        }
    }
    else
    {
        // Thread khác đang nạp cache, đợi 50ms và đọc lại cache
        await Task.Delay(50);
        return await GetProductAsync(productId);
    }
}
```

---

## 3. High-Speed Shopping Cart on Redis
Giỏ hàng đòi hỏi tốc độ phản hồi cực nhanh khi người dùng thêm bớt số lượng liên tục:
- Lưu trữ dạng `Redis Hash`: Key `cart:user:{userId}`.
- Mỗi Field trong Hash là một `skuId`.
- Giá trị Field là JSON bao gồm: `quantity`, `added_at`, `price_snapshot`.
- Khi user hoàn tất thanh toán hoặc xóa giỏ hàng: Gọi `DEL cart:user:{userId}`.
- Định kỳ có background job đồng bộ giỏ hàng chưa mua vào PostgreSQL để phân tích dữ liệu abandoned cart.

---

## 4. Flash Sale Atomic Lua Script
Để xử lý 100,000 user tranh mua 100 sản phẩm mà không cần chạm vào DB:

```lua
-- KEYS[1]: flash:sku:{skuId}:stock
-- ARGV[1]: Số lượng cần mua (thường là 1)
-- ARGV[2]: userId (để chống 1 người mua nhiều lần)

local stockKey = KEYS[1]
local requestedQty = tonumber(ARGV[1])
local userKey = "flash:user:" .. ARGV[2] .. ":" .. stockKey

-- Kiểm tra xem user đã mua trong đợt flash sale này chưa
if redis.call('EXISTS', userKey) == 1 then
    return -2 -- Error: User đã mua rồi
end

local currentStock = tonumber(redis.call('GET', stockKey) or "0")
if currentStock < requestedQty then
    return -1 -- Error: Hết hàng (Sold out)
end

-- Trừ tồn kho nguyên tử trên Redis
redis.call('DECRBY', stockKey, requestedQty)
-- Đánh dấu user đã mua thành công
redis.call('SET', userKey, "1", "EX", 3600)

return 1 -- Thành công!
```
Script này chạy nguyên tử (Single-threaded) trong Redis Engine với tốc độ > 80,000 ops/giây. Chỉ 100 người nhận được return code `1`, những người còn lại bị chặn đứng ngay lập tức với độ trễ < 2ms.
