using ECommerce.Modules.Product.Application;
using ECommerce.Modules.Product.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Product.API;

public static class ProductModuleExtensions
{
    public static IServiceCollection AddProductModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<ProductDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "product");
            });
        });

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ProductModuleExtensions).Assembly));
        return services;
    }

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products").WithTags("Products");

        group.MapPost("/", async (CreateProductCommand command, ISender sender) =>
        {
            var id = await sender.Send(command);
            return Results.Created($"/api/v1/products/{id}", new { id });
        }).WithName("CreateProduct");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var product = await sender.Send(new GetProductByIdQuery(id));
            return product != null ? Results.Ok(product) : Results.NotFound();
        }).WithName("GetProductById");

        group.MapGet("/", async (int? page, int? pageSize, Guid? categoryId, ISender sender) =>
        {
            var products = await sender.Send(new GetProductsQuery(page ?? 1, pageSize ?? 20, categoryId));
            return Results.Ok(products);
        }).WithName("GetProducts");

        return app;
    }
}
