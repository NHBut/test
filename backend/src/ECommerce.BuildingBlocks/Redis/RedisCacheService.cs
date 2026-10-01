using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ECommerce.BuildingBlocks.Redis;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> factory, TimeSpan? ttl = null, CancellationToken ct = default);
    Task<int> ExecuteFlashSaleReservationAsync(string skuId, string userId, int quantity);
}

public class RedisCacheService : IRedisCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisCacheService> _logger;
    private const string NullPlaceholder = "__NULL_OBJECT__";

    public RedisCacheService(IConfiguration configuration, ILogger<RedisCacheService> logger)
    {
        _logger = logger;
        var connectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379,abortConnect=false";
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (!value.HasValue) return default;
            if (value.ToString() == NullPlaceholder) return default;

            return JsonSerializer.Deserialize<T>(value.ToString()!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis GET error for key {Key}. Graceful fallback to null.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            var effectiveTtl = ttl ?? TimeSpan.FromHours(1);
            // Thêm jitter ngẫu nhiên để chống Cache Stampede khi hết hạn đồng loạt
            var jitter = TimeSpan.FromSeconds(new Random().Next(10, 180));
            effectiveTtl = effectiveTtl + jitter;

            var json = value == null ? NullPlaceholder : JsonSerializer.Serialize(value);
            await _db.StringSetAsync(key, json, effectiveTtl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SET error for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis DEL error for key {Key}", key);
        }
    }

    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        // 1. Thử đọc từ Redis
        var cached = await GetAsync<T>(key, ct);
        if (cached != null) return cached;

        // 2. Chống Cache Stampede: Khóa phân tán ngắn hạn 3s
        var lockKey = $"lock:{key}";
        var acquired = await _db.StringSetAsync(lockKey, "1", TimeSpan.FromSeconds(3), When.NotExists);

        if (acquired)
        {
            try
            {
                // Gọi DB
                var result = await factory();
                if (result == null)
                {
                    // Chống Cache Penetration: lưu null placeholder trong 2 phút
                    await _db.StringSetAsync(key, NullPlaceholder, TimeSpan.FromMinutes(2));
                    return default;
                }

                await SetAsync(key, result, ttl, ct);
                return result;
            }
            finally
            {
                await _db.KeyDeleteAsync(lockKey);
            }
        }
        else
        {
            // Thread khác đang nạp cache, đợi 50ms rồi thử lại
            await Task.Delay(50, ct);
            return await GetAsync<T>(key, ct);
        }
    }

    public async Task<int> ExecuteFlashSaleReservationAsync(string skuId, string userId, int quantity)
    {
        // Lua Script: Atomic check user bought + stock check + stock decrement
        const string script = @"
            local stockKey = KEYS[1]
            local userKey = 'flash:user:' .. ARGV[2] .. ':' .. stockKey
            local requestedQty = tonumber(ARGV[1])

            if redis.call('EXISTS', userKey) == 1 then
                return -2 -- Already purchased
            end

            local currentStock = tonumber(redis.call('GET', stockKey) or '0')
            if currentStock < requestedQty then
                return -1 -- Out of stock
            end

            redis.call('DECRBY', stockKey, requestedQty)
            redis.call('SET', userKey, '1', 'EX', 3600)
            return 1 -- Success
        ";

        var stockKey = $"flash:sku:{skuId}:stock";
        var res = await _db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { stockKey },
            new RedisValue[] { quantity, userId });

        return (int)res;
    }
}
