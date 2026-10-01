using ECommerce.BuildingBlocks.CQRS;
using ECommerce.Modules.Inventory.Domain;
using ECommerce.Modules.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Modules.Inventory.Application;

public record ReserveItemDto(Guid SkuId, int Quantity);

public record ReserveInventoryCommand(Guid OrderId, List<ReserveItemDto> Items, int TtlMinutes = 15) : ICommand<bool>;

public record ConfirmReservationCommand(Guid OrderId, Guid SkuId) : ICommand<bool>;

public record ReleaseReservationCommand(Guid OrderId, Guid SkuId) : ICommand<bool>;

public record SetStockCommand(Guid SkuId, int Quantity) : ICommand<bool>;

public class InventoryHandlers :
    ICommandHandler<ReserveInventoryCommand, bool>,
    ICommandHandler<ConfirmReservationCommand, bool>,
    ICommandHandler<ReleaseReservationCommand, bool>,
    ICommandHandler<SetStockCommand, bool>
{
    private readonly InventoryDbContext _db;
    private readonly IInventoryRepository _repository;

    public InventoryHandlers(InventoryDbContext db, IInventoryRepository repository)
    {
        _db = db;
        _repository = repository;
    }

    public async Task<bool> Handle(ReserveInventoryCommand request, CancellationToken cancellationToken)
    {
        // Deadlock Prevention: Sắp xếp SkuId tăng dần để đảm bảo thứ tự khóa hàng nguyên tử
        var sortedItems = request.Items.OrderBy(x => x.SkuId).ToList();
        var reservedItems = new List<ReserveItemDto>();

        foreach (var item in sortedItems)
        {
            var success = await _repository.TryReserveStockAtomicAsync(
                request.OrderId,
                item.SkuId,
                item.Quantity,
                TimeSpan.FromMinutes(request.TtlMinutes),
                cancellationToken);

            if (!success)
            {
                // Nếu một item không đủ hàng, nhả lại toàn bộ các item đã reserve trước đó (Compensating actions)
                foreach (var previouslyReserved in reservedItems)
                {
                    await _repository.ReleaseReservationAsync(request.OrderId, previouslyReserved.SkuId, cancellationToken);
                }
                return false;
            }

            reservedItems.Add(item);
        }

        return true;
    }

    public async Task<bool> Handle(ConfirmReservationCommand request, CancellationToken cancellationToken)
    {
        return await _repository.ConfirmReservationAsync(request.OrderId, request.SkuId, cancellationToken);
    }

    public async Task<bool> Handle(ReleaseReservationCommand request, CancellationToken cancellationToken)
    {
        return await _repository.ReleaseReservationAsync(request.OrderId, request.SkuId, cancellationToken);
    }

    public async Task<bool> Handle(SetStockCommand request, CancellationToken cancellationToken)
    {
        var inv = await _db.ProductInventories.FirstOrDefaultAsync(x => x.SkuId == request.SkuId, cancellationToken);
        if (inv == null)
        {
            inv = ProductInventory.Create(request.SkuId, request.Quantity);
            _db.ProductInventories.Add(inv);
        }
        else
        {
            // Trực tiếp set available quantity
            await _db.Database.ExecuteSqlRawAsync(
                @"UPDATE inventory.product_inventories 
                  SET ""AvailableQuantity"" = @qty, ""UpdatedAt"" = NOW() 
                  WHERE ""SkuId"" = @skuId",
                new Npgsql.NpgsqlParameter("@qty", request.Quantity),
                new Npgsql.NpgsqlParameter("@skuId", request.SkuId));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
