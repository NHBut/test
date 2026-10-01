using ECommerce.BuildingBlocks.Database;
using ECommerce.BuildingBlocks.Kafka;
using ECommerce.BuildingBlocks.Middlewares;
using ECommerce.BuildingBlocks.Outbox;
using ECommerce.BuildingBlocks.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.BuildingBlocks;

public static class BuildingBlocksExtensions
{
    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services, IConfiguration configuration)
    {
        // Common PostgreSQL DbContext
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;MaxPoolSize=100;";

        services.AddDbContext<CommonDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "common");
            });
        });

        // Redis Service
        services.AddSingleton<IRedisCacheService, RedisCacheService>();

        // Kafka Producer
        services.AddSingleton<IKafkaProducer, KafkaProducer>();

        // Outbox Pattern
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddHostedService<OutboxBackgroundProcessor>();

        return services;
    }

    public static IApplicationBuilder UseBuildingBlocks(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseMiddleware<IdempotencyMiddleware>();
        return app;
    }
}
