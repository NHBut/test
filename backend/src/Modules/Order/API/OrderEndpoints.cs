using ECommerce.Modules.Order.Application;
using ECommerce.Modules.Order.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Order.API;

public static class OrderModuleExtensions
{
    public static IServiceCollection AddOrderModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<OrderDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "orders");
            });
        });

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(OrderModuleExtensions).Assembly));
        return services;
    }

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders").WithTags("Orders");

        group.MapPost("/", async (CreateOrderCommand command, ISender sender) =>
        {
            var orderId = await sender.Send(command);
            return Results.Created($"/api/v1/orders/{orderId}", new { orderId });
        }).WithName("CreateOrder");

        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelOrderRequest req, ISender sender) =>
        {
            var success = await sender.Send(new CancelOrderCommand(id, req.Reason ?? "Customer request"));
            return success ? Results.Ok(new { message = "Order cancelled successfully" }) : Results.NotFound();
        }).WithName("CancelOrder");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var order = await sender.Send(new GetOrderByIdQuery(id));
            return order != null ? Results.Ok(order) : Results.NotFound();
        }).WithName("GetOrderById");

        group.MapGet("/customer/{customerId:guid}", async (Guid customerId, int? page, int? pageSize, ISender sender) =>
        {
            var orders = await sender.Send(new GetCustomerOrdersQuery(customerId, page ?? 1, pageSize ?? 20));
            return Results.Ok(orders);
        }).WithName("GetCustomerOrders");

        return app;
    }
}

public record CancelOrderRequest(string? Reason);
