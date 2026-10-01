using ECommerce.BuildingBlocks.CQRS;
using ECommerce.BuildingBlocks.Database;
using ECommerce.BuildingBlocks.Outbox;
using ECommerce.Modules.Order.Domain;
using ECommerce.Modules.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Modules.Order.Application;

public record CreateOrderItemDto(Guid SkuId, string Sku, string ProductName, string VariantName, decimal UnitPrice, int Quantity);

public record CreateOrderCommand(
    Guid CustomerId,
    string ShippingAddressJson,
    List<CreateOrderItemDto> Items,
    decimal ShippingFee = 0,
    decimal DiscountAmount = 0
) : ICommand<Guid>;

public record CancelOrderCommand(Guid OrderId, string Reason) : ICommand<bool>;

public record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string Status,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingFee,
    decimal TotalAmount,
    string ShippingAddressJson,
    DateTime CreatedAt,
    List<OrderItemDto> Items
);

public record OrderItemDto(Guid Id, Guid SkuId, string ProductName, string VariantName, string Sku, decimal UnitPrice, int Quantity, decimal TotalPrice);

public record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto?>;

public record GetCustomerOrdersQuery(Guid CustomerId, int Page = 1, int PageSize = 20) : IQuery<List<OrderDto>>;

public class OrderHandlers :
    ICommandHandler<CreateOrderCommand, Guid>,
    ICommandHandler<CancelOrderCommand, bool>,
    IQueryHandler<GetOrderByIdQuery, OrderDto?>,
    IQueryHandler<GetCustomerOrdersQuery, List<OrderDto>>
{
    private readonly OrderDbContext _orderDb;
    private readonly CommonDbContext _commonDb;
    private readonly IOutboxService _outbox;

    public OrderHandlers(OrderDbContext orderDb, CommonDbContext commonDb, IOutboxService outbox)
    {
        _orderDb = orderDb;
        _commonDb = commonDb;
        _outbox = outbox;
    }

    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = Domain.Order.Create(request.CustomerId, request.ShippingAddressJson, request.ShippingFee, request.DiscountAmount);

        foreach (var item in request.Items)
        {
            order.AddItem(item.SkuId, item.ProductName, item.VariantName, item.Sku, item.UnitPrice, item.Quantity);
        }

        order.CompleteCreation();

        _orderDb.Orders.Add(order);
        // Transactional Outbox Pattern: Domain event được enqueue vào common.outbox_events trong cùng transaction
        _outbox.EnqueueDomainEvent(order, _commonDb);

        await _orderDb.SaveChangesAsync(cancellationToken);
        await _commonDb.SaveChangesAsync(cancellationToken);

        return order.Id;
    }

    public async Task<bool> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderDb.Orders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken);

        if (order == null) return false;

        order.Cancel(request.Reason);
        _outbox.EnqueueDomainEvent(order, _commonDb);

        await _orderDb.SaveChangesAsync(cancellationToken);
        await _commonDb.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _orderDb.Orders
            .Include(x => x.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken);

        if (order == null) return null;

        return MapToDto(order);
    }

    public async Task<List<OrderDto>> Handle(GetCustomerOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await _orderDb.Orders
            .Include(x => x.Items)
            .AsNoTracking()
            .Where(x => x.CustomerId == request.CustomerId)
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return orders.Select(MapToDto).ToList();
    }

    private static OrderDto MapToDto(Domain.Order order)
    {
        return new OrderDto(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.Status.ToString(),
            order.Subtotal,
            order.DiscountAmount,
            order.ShippingFee,
            order.TotalAmount,
            order.ShippingAddressJson,
            order.CreatedAt,
            order.Items.Select(i => new OrderItemDto(
                i.Id,
                i.SkuId,
                i.ProductName,
                i.VariantName,
                i.Sku,
                i.UnitPrice,
                i.Quantity,
                i.TotalPrice
            )).ToList()
        );
    }
}
