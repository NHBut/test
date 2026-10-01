using ECommerce.Modules.Payment.Application;
using ECommerce.Modules.Payment.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Payment.API;

public static class PaymentModuleExtensions
{
    public static IServiceCollection AddPaymentModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<PaymentDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "payment");
            });
        });

        services.AddSingleton<IPaymentGatewayService, MockPaymentGatewayService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PaymentModuleExtensions).Assembly));
        return services;
    }

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments").WithTags("Payment");

        group.MapPost("/", async (InitiatePaymentCommand command, ISender sender) =>
        {
            var paymentUrl = await sender.Send(command);
            return Results.Ok(new { paymentUrl });
        }).WithName("InitiatePayment");

        group.MapPost("/webhook", async (WebhookCommand command, ISender sender) =>
        {
            await sender.Send(command);
            return Results.Ok(new { status = "RECEIVED" });
        }).WithName("PaymentWebhook");

        group.MapPost("/refund", async (RefundPaymentCommand command, ISender sender) =>
        {
            var success = await sender.Send(command);
            return success ? Results.Ok(new { message = "Refund processed successfully" }) : Results.NotFound();
        }).WithName("RefundPayment");

        return app;
    }
}
