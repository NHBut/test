using ECommerce.BuildingBlocks.Domain;

namespace ECommerce.Modules.Order.Domain;

public enum OrderStatus
{
    PENDING_PAYMENT,
    PAID,
    CONFIRMED,
    PROCESSING,
    SHIPPING,
    DELIVERED,
    CANCELLED,
    REFUNDED
}

public record OrderCreatedDomainEvent(Guid OrderId, Guid CustomerId, decimal TotalAmount, List<OrderItemSnapshot> Items) : IDomainEvent;
public record OrderPaidDomainEvent(Guid OrderId, decimal Amount) : IDomainEvent;
public record OrderCancelledDomainEvent(Guid OrderId, string Reason) : IDomainEvent;
public record OrderItemSnapshot(Guid SkuId, string Sku, int Quantity, decimal UnitPrice);

public class Order : AggregateRoot<Guid>
{
    public string OrderNumber { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.PENDING_PAYMENT;
    public decimal Subtotal { get; private set; }
    public decimal DiscountAmount { get; private set; } = 0;
    public decimal ShippingFee { get; private set; } = 0;
    public decimal TotalAmount { get; private set; }
    public string ShippingAddressJson { get; private set; } = "{}";
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private readonly List<OrderStatusHistory> _statusHistory = new();
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    private Order() { }

    public static Order Create(Guid customerId, string shippingAddressJson, decimal shippingFee = 0, decimal discountAmount = 0)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}",
            CustomerId = customerId,
            Status = OrderStatus.PENDING_PAYMENT,
            ShippingAddressJson = shippingAddressJson,
            ShippingFee = shippingFee,
            DiscountAmount = discountAmount,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        order._statusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = null,
            ToStatus = OrderStatus.PENDING_PAYMENT,
            Reason = "Order initialized",
            ChangedAt = DateTime.UtcNow
        });

        return order;
    }

    public void AddItem(Guid skuId, string productName, string variantName, string sku, decimal unitPrice, int quantity)
    {
        if (Status != OrderStatus.PENDING_PAYMENT)
            throw new InvalidOperationException("Cannot add items to an order that is not in PENDING_PAYMENT state.");

        if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.");
        if (unitPrice < 0) throw new ArgumentException("Unit price cannot be negative.");

        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = Id,
            SkuId = skuId,
            ProductName = productName,
            VariantName = variantName,
            Sku = sku,
            UnitPrice = unitPrice,
            Quantity = quantity,
            TotalPrice = unitPrice * quantity
        };

        _items.Add(item);
        RecalculateTotals();
    }

    public void CompleteCreation()
    {
        if (_items.Count == 0)
            throw new InvalidOperationException("Order must have at least one item.");

        AddDomainEvent(new OrderCreatedDomainEvent(
            Id, 
            CustomerId, 
            TotalAmount, 
            _items.Select(x => new OrderItemSnapshot(x.SkuId, x.Sku, x.Quantity, x.UnitPrice)).ToList()
        ));
    }

    // STATE MACHINE TRANSITIONS
    public void MarkAsPaid()
    {
        TransitionTo(OrderStatus.PAID, "Payment successfully confirmed via webhook/gateway");
        AddDomainEvent(new OrderPaidDomainEvent(Id, TotalAmount));
    }

    public void Confirm()
    {
        TransitionTo(OrderStatus.CONFIRMED, "Order confirmed by merchant/system");
    }

    public void StartProcessing()
    {
        TransitionTo(OrderStatus.PROCESSING, "Warehouse started packing items");
    }

    public void Ship()
    {
        TransitionTo(OrderStatus.SHIPPING, "Handed over to delivery carrier");
    }

    public void MarkAsDelivered()
    {
        TransitionTo(OrderStatus.DELIVERED, "Customer successfully received package");
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.DELIVERED || Status == OrderStatus.REFUNDED)
            throw new InvalidOperationException($"Cannot cancel order in {Status} state.");

        TransitionTo(OrderStatus.CANCELLED, reason);
        AddDomainEvent(new OrderCancelledDomainEvent(Id, reason));
    }

    public void Refund(string reason)
    {
        if (Status != OrderStatus.PAID && Status != OrderStatus.CONFIRMED && Status != OrderStatus.DELIVERED)
            throw new InvalidOperationException($"Cannot refund order in {Status} state.");

        TransitionTo(OrderStatus.REFUNDED, reason);
    }

    private void TransitionTo(OrderStatus newStatus, string reason)
    {
        var isValid = (Status, newStatus) switch
        {
            (OrderStatus.PENDING_PAYMENT, OrderStatus.PAID) => true,
            (OrderStatus.PENDING_PAYMENT, OrderStatus.CANCELLED) => true,
            (OrderStatus.PAID, OrderStatus.CONFIRMED) => true,
            (OrderStatus.PAID, OrderStatus.REFUNDED) => true,
            (OrderStatus.CONFIRMED, OrderStatus.PROCESSING) => true,
            (OrderStatus.CONFIRMED, OrderStatus.CANCELLED) => true,
            (OrderStatus.PROCESSING, OrderStatus.SHIPPING) => true,
            (OrderStatus.PROCESSING, OrderStatus.CANCELLED) => true,
            (OrderStatus.SHIPPING, OrderStatus.DELIVERED) => true,
            (OrderStatus.SHIPPING, OrderStatus.CANCELLED) => true,
            (OrderStatus.DELIVERED, OrderStatus.REFUNDED) => true,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException($"Invalid order state transition from {Status} to {newStatus}.");
        }

        var oldStatus = Status;
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;

        _statusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = Id,
            FromStatus = oldStatus,
            ToStatus = newStatus,
            Reason = reason,
            ChangedAt = DateTime.UtcNow
        });
    }

    private void RecalculateTotals()
    {
        Subtotal = _items.Sum(x => x.TotalPrice);
        TotalAmount = Math.Max(0, Subtotal - DiscountAmount + ShippingFee);
    }
}

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid SkuId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
}

public class OrderStatusHistory
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
