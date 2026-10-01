using ECommerce.Modules.Identity.Application;
using ECommerce.Modules.Identity.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Identity.API;

public static class IdentityModuleExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<IdentityDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
            });
        });

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(IdentityModuleExtensions).Assembly));

        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/register", async (RegisterCommand command, ISender sender) =>
        {
            var response = await sender.Send(command);
            return Results.Ok(response);
        }).WithName("RegisterUser");

        group.MapPost("/login", async (LoginCommand command, ISender sender) =>
        {
            var response = await sender.Send(command);
            return Results.Ok(response);
        }).WithName("LoginUser");

        group.MapPost("/refresh", async (RefreshTokenCommand command, ISender sender) =>
        {
            var response = await sender.Send(command);
            return Results.Ok(response);
        }).WithName("RefreshToken");

        return app;
    }
}
