using ECommerce.Modules.Inventory.Application;
using ECommerce.Modules.Inventory.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Inventory.API;

public static class InventoryModuleExtensions
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<InventoryDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "inventory");
            });
        });

        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddHostedService<InventoryReservationExpiryWorker>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(InventoryModuleExtensions).Assembly));

        return services;
    }

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory").WithTags("Inventory");

        group.MapPost("/reserve", async (ReserveInventoryCommand command, ISender sender) =>
        {
            var success = await sender.Send(command);
            return success ? Results.Ok(new { message = "Stock reserved successfully" }) 
                           : Results.Conflict(new { error = "OUT_OF_STOCK", message = "One or more items are out of stock" });
        }).WithName("ReserveInventory");

        group.MapPost("/confirm", async (ConfirmReservationCommand command, ISender sender) =>
        {
            var success = await sender.Send(command);
            return success ? Results.Ok(new { message = "Reservation confirmed" }) : Results.NotFound();
        }).WithName("ConfirmReservation");

        group.MapPost("/release", async (ReleaseReservationCommand command, ISender sender) =>
        {
            var success = await sender.Send(command);
            return success ? Results.Ok(new { message = "Reservation released" }) : Results.NotFound();
        }).WithName("ReleaseReservation");

        group.MapPost("/stock", async (SetStockCommand command, ISender sender) =>
        {
            await sender.Send(command);
            return Results.Ok(new { message = "Stock updated successfully" });
        }).WithName("SetStock");

        return app;
    }
}
