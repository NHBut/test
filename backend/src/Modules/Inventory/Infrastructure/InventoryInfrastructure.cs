using ECommerce.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ECommerce.Modules.Inventory.Infrastructure;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<ProductInventory> ProductInventories => Set<ProductInventory>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("inventory");

        modelBuilder.Entity<ProductInventory>(b =>
        {
            b.ToTable("product_inventories");
            b.HasKey(x => x.Id);
            b.Property(x => x.SkuId).IsRequired();
            b.HasIndex(x => x.SkuId).IsUnique();
            b.Property(x => x.AvailableQuantity).IsRequired();
            b.Property(x => x.ReservedQuantity).IsRequired();
        });

        modelBuilder.Entity<InventoryReservation>(b =>
        {
            b.ToTable("inventory_reservations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasMaxLength(30).IsRequired();
            b.HasIndex(x => new { x.OrderId, x.SkuId }).IsUnique();

            // Partial index cho Background Worker quét reservation hết hạn
            b.HasIndex(x => x.ExpiresAt)
             .HasFilter("\"Status\" = 'PENDING'")
             .HasDatabaseName("idx_reservations_pending_expired");
        });
    }
}

public interface IInventoryRepository
{
    Task<bool> TryReserveStockAtomicAsync(Guid orderId, Guid skuId, int quantity, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> ConfirmReservationAsync(Guid orderId, Guid skuId, CancellationToken ct = default);
    Task<bool> ReleaseReservationAsync(Guid orderId, Guid skuId, CancellationToken ct = default);
}

public class InventoryRepository : IInventoryRepository
{
    private readonly InventoryDbContext _db;
    private readonly ILogger<InventoryRepository> _logger;

    public InventoryRepository(InventoryDbContext db, ILogger<InventoryRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> TryReserveStockAtomicAsync(Guid orderId, Guid skuId, int quantity, TimeSpan ttl, CancellationToken ct = default)
    {
        // Kiểm tra xem đơn hàng này đã reserve SKU này trước đó chưa (Chống retry duplicate)
        var existingReservation = await _db.InventoryReservations
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.SkuId == skuId, ct);

        if (existingReservation != null)
        {
            if (existingReservation.Status == "PENDING") return true; // Đã reserve rồi, coi như thành công
            return false;
        }

        // Bắt đầu Transaction
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Atomic Conditional Update: Trừ kho trực tiếp trong PostgreSQL Engine
        const string sql = @"
            UPDATE inventory.product_inventories
            SET ""AvailableQuantity"" = ""AvailableQuantity"" - @qty,
                ""ReservedQuantity"" = ""ReservedQuantity"" + @qty,
                ""Version"" = ""Version"" + 1,
                ""UpdatedAt"" = NOW()
            WHERE ""SkuId"" = @skuId 
              AND ""AvailableQuantity"" >= @qty;
        ";

        var rowsAffected = await _db.Database.ExecuteSqlRawAsync(
            sql, 
            new NpgsqlParameter("@qty", quantity),
            new NpgsqlParameter("@skuId", skuId)
        );

        if (rowsAffected == 0)
        {
            // Hết hàng hoặc không đủ số lượng
            await tx.RollbackAsync(ct);
            _logger.LogWarning("Insufficient stock for SkuId {SkuId}. Requested: {Qty}", skuId, quantity);
            return false;
        }

        // Tạo bản ghi Reservation
        var reservation = new InventoryReservation
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            SkuId = skuId,
            Quantity = quantity,
            Status = "PENDING",
            ExpiresAt = DateTime.UtcNow.Add(ttl),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.InventoryReservations.Add(reservation);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Stock reserved successfully for Order {OrderId}, SKU {SkuId}, Qty {Qty}", orderId, skuId, quantity);
        return true;
    }

    public async Task<bool> ConfirmReservationAsync(Guid orderId, Guid skuId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var reservation = await _db.InventoryReservations
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.SkuId == skuId && x.Status == "PENDING", ct);

        if (reservation == null) return false;

        const string sql = @"
            UPDATE inventory.product_inventories
            SET ""ReservedQuantity"" = ""ReservedQuantity"" - @qty,
                ""UpdatedAt"" = NOW()
            WHERE ""SkuId"" = @skuId;
        ";

        await _db.Database.ExecuteSqlRawAsync(
            sql, 
            new NpgsqlParameter("@qty", reservation.Quantity),
            new NpgsqlParameter("@skuId", skuId)
        );

        reservation.Status = "CONFIRMED";
        reservation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return true;
    }

    public async Task<bool> ReleaseReservationAsync(Guid orderId, Guid skuId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var reservation = await _db.InventoryReservations
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.SkuId == skuId && x.Status == "PENDING", ct);

        if (reservation == null) return false;

        const string sql = @"
            UPDATE inventory.product_inventories
            SET ""AvailableQuantity"" = ""AvailableQuantity"" + @qty,
                ""ReservedQuantity"" = ""ReservedQuantity"" - @qty,
                ""UpdatedAt"" = NOW()
            WHERE ""SkuId"" = @skuId;
        ";

        await _db.Database.ExecuteSqlRawAsync(
            sql, 
            new NpgsqlParameter("@qty", reservation.Quantity),
            new NpgsqlParameter("@skuId", skuId)
        );

        reservation.Status = "RELEASED";
        reservation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return true;
    }
}

public class InventoryReservationExpiryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InventoryReservationExpiryWorker> _logger;

    public InventoryReservationExpiryWorker(IServiceProvider serviceProvider, ILogger<InventoryReservationExpiryWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _logger.LogInformation("InventoryReservationExpiryWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

                // Quét các reservation quá hạn 15 phút
                var expiredReservations = await db.InventoryReservations
                    .FromSqlRaw(@"
                        SELECT * FROM inventory.inventory_reservations
                        WHERE ""Status"" = 'PENDING' 
                          AND ""ExpiresAt"" < NOW()
                        LIMIT 50
                        FOR UPDATE SKIP LOCKED")
                    .ToListAsync(stoppingToken);

                if (expiredReservations.Count > 0)
                {
                    foreach (var res in expiredReservations)
                    {
                        const string restoreSql = @"
                            UPDATE inventory.product_inventories
                            SET ""AvailableQuantity"" = ""AvailableQuantity"" + @qty,
                                ""ReservedQuantity"" = ""ReservedQuantity"" - @qty,
                                ""UpdatedAt"" = NOW()
                            WHERE ""SkuId"" = @skuId;
                        ";

                        await db.Database.ExecuteSqlRawAsync(
                            restoreSql,
                            new NpgsqlParameter("@qty", res.Quantity),
                            new NpgsqlParameter("@skuId", res.SkuId)
                        );

                        res.Status = "EXPIRED";
                        res.UpdatedAt = DateTime.UtcNow;

                        _logger.LogInformation("Restored expired reservation {ReservationId} for SKU {SkuId} (Qty: {Qty})", res.Id, res.SkuId, res.Quantity);
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in InventoryReservationExpiryWorker");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
