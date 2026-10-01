using ECommerce.BuildingBlocks.Domain;

namespace ECommerce.Modules.Inventory.Domain;

public record InventoryReservedDomainEvent(Guid OrderId, Guid SkuId, int Quantity) : IDomainEvent;
public record InventoryReleasedDomainEvent(Guid OrderId, Guid SkuId, int Quantity) : IDomainEvent;

public class ProductInventory : AggregateRoot<Guid>
{
    public Guid SkuId { get; private set; }
    public int AvailableQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private ProductInventory() { }

    public static ProductInventory Create(Guid skuId, int initialStock)
    {
        if (initialStock < 0) throw new ArgumentException("Initial stock cannot be negative.");
        return new ProductInventory
        {
            Id = Guid.NewGuid(),
            SkuId = skuId,
            AvailableQuantity = initialStock,
            ReservedQuantity = 0,
            Version = 1,
            UpdatedAt = DateTime.UtcNow
        };
    }
}

public class InventoryReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid SkuId { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING, CONFIRMED, RELEASED, EXPIRED
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
