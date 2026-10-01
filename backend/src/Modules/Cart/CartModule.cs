using System.Text.Json;
using ECommerce.BuildingBlocks.CQRS;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ECommerce.Modules.Cart;

public record CartItem(Guid SkuId, string Sku, string ProductName, decimal UnitPrice, int Quantity, DateTime AddedAt);

public record CartDto(Guid UserId, List<CartItem> Items, decimal TotalAmount);

public interface ICartRepository
{
    Task<CartDto> GetCartAsync(Guid userId);
    Task AddOrUpdateItemAsync(Guid userId, CartItem item);
    Task RemoveItemAsync(Guid userId, Guid skuId);
    Task ClearCartAsync(Guid userId);
}

public class RedisCartRepository : ICartRepository
{
    private readonly IDatabase _db;

    public RedisCartRepository(IConfiguration configuration)
    {
        var connectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379,abortConnect=false";
        var redis = ConnectionMultiplexer.Connect(connectionString);
        _db = redis.GetDatabase();
    }

    private static string GetCartKey(Guid userId) => $"cart:user:{userId}";

    public async Task<CartDto> GetCartAsync(Guid userId)
    {
        var key = GetCartKey(userId);
        var entries = await _db.HashGetAllAsync(key);

        var items = new List<CartItem>();
        foreach (var entry in entries)
        {
            if (entry.Value.HasValue)
            {
                var item = JsonSerializer.Deserialize<CartItem>(entry.Value.ToString()!);
                if (item != null) items.Add(item);
            }
        }

        var total = items.Sum(i => i.UnitPrice * i.Quantity);
        return new CartDto(userId, items, total);
    }

    public async Task AddOrUpdateItemAsync(Guid userId, CartItem item)
    {
        var key = GetCartKey(userId);
        var field = item.SkuId.ToString();

        // Kiểm tra xem item đã có trong giỏ chưa để cộng dồn số lượng
        var existing = await _db.HashGetAsync(key, field);
        if (existing.HasValue)
        {
            var existingItem = JsonSerializer.Deserialize<CartItem>(existing.ToString()!);
            if (existingItem != null)
            {
                item = item with { Quantity = existingItem.Quantity + item.Quantity };
            }
        }

        var json = JsonSerializer.Serialize(item);
        await _db.HashSetAsync(key, field, json);
        await _db.KeyExpireAsync(key, TimeSpan.FromDays(30)); // 30 ngày TTL
    }

    public async Task RemoveItemAsync(Guid userId, Guid skuId)
    {
        var key = GetCartKey(userId);
        await _db.HashDeleteAsync(key, skuId.ToString());
    }

    public async Task ClearCartAsync(Guid userId)
    {
        var key = GetCartKey(userId);
        await _db.KeyDeleteAsync(key);
    }
}

// CQRS Handlers
public record AddToCartCommand(Guid UserId, Guid SkuId, string Sku, string ProductName, decimal UnitPrice, int Quantity) : ICommand<CartDto>;
public record RemoveFromCartCommand(Guid UserId, Guid SkuId) : ICommand<CartDto>;
public record GetCartQuery(Guid UserId) : IQuery<CartDto>;
public record ClearCartCommand(Guid UserId) : ICommand<bool>;

public class CartHandlers :
    ICommandHandler<AddToCartCommand, CartDto>,
    ICommandHandler<RemoveFromCartCommand, CartDto>,
    IQueryHandler<GetCartQuery, CartDto>,
    ICommandHandler<ClearCartCommand, bool>
{
    private readonly ICartRepository _cartRepo;

    public CartHandlers(ICartRepository cartRepo) => _cartRepo = cartRepo;

    public async Task<CartDto> Handle(AddToCartCommand request, CancellationToken cancellationToken)
    {
        var item = new CartItem(request.SkuId, request.Sku, request.ProductName, request.UnitPrice, request.Quantity, DateTime.UtcNow);
        await _cartRepo.AddOrUpdateItemAsync(request.UserId, item);
        return await _cartRepo.GetCartAsync(request.UserId);
    }

    public async Task<CartDto> Handle(RemoveFromCartCommand request, CancellationToken cancellationToken)
    {
        await _cartRepo.RemoveItemAsync(request.UserId, request.SkuId);
        return await _cartRepo.GetCartAsync(request.UserId);
    }

    public async Task<CartDto> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        return await _cartRepo.GetCartAsync(request.UserId);
    }

    public async Task<bool> Handle(ClearCartCommand request, CancellationToken cancellationToken)
    {
        await _cartRepo.ClearCartAsync(request.UserId);
        return true;
    }
}

// Extensions & Endpoints
public static class CartModuleExtensions
{
    public static IServiceCollection AddCartModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICartRepository, RedisCartRepository>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CartModuleExtensions).Assembly));
        return services;
    }

    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cart").WithTags("Cart");

        group.MapGet("/{userId:guid}", async (Guid userId, ISender sender) =>
        {
            var cart = await sender.Send(new GetCartQuery(userId));
            return Results.Ok(cart);
        }).WithName("GetCart");

        group.MapPost("/items", async (AddToCartCommand command, ISender sender) =>
        {
            var cart = await sender.Send(command);
            return Results.Ok(cart);
        }).WithName("AddToCart");

        group.MapDelete("/{userId:guid}/items/{skuId:guid}", async (Guid userId, Guid skuId, ISender sender) =>
        {
            var cart = await sender.Send(new RemoveFromCartCommand(userId, skuId));
            return Results.Ok(cart);
        }).WithName("RemoveFromCart");

        group.MapDelete("/{userId:guid}", async (Guid userId, ISender sender) =>
        {
            await sender.Send(new ClearCartCommand(userId));
            return Results.Ok(new { message = "Cart cleared" });
        }).WithName("ClearCart");

        return app;
    }
}
