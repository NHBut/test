using ECommerce.BuildingBlocks;
using ECommerce.Modules.Cart;
using ECommerce.Modules.Identity.API;
using ECommerce.Modules.Inventory.API;
using ECommerce.Modules.Notification;
using ECommerce.Modules.Order.API;
using ECommerce.Modules.Payment.API;
using ECommerce.Modules.Product.API;
using ECommerce.Modules.Promotion;
using ECommerce.Modules.Review;
using ECommerce.Modules.Search;
using ECommerce.Modules.Shipping;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Structured Logging với Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"));

// 2. Authentication & Authorization
var jwtSecret = builder.Configuration["Jwt:SecretKey"] ?? "SUPER_SECURE_JWT_SECRET_KEY_FOR_PRODUCTION_SCALING_32BYTES_LONG";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ecommerce-api",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ecommerce-web",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });
builder.Services.AddAuthorization();

// 3. CORS Policy for SPA Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 4. Register Shared BuildingBlocks
builder.Services.AddBuildingBlocks(builder.Configuration);

// 5. Register Autonomous Bounded Context Modules
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddProductModule(builder.Configuration);
builder.Services.AddInventoryModule(builder.Configuration);
builder.Services.AddCartModule(builder.Configuration);
builder.Services.AddOrderModule(builder.Configuration);
builder.Services.AddPaymentModule(builder.Configuration);
builder.Services.AddPromotionModule(builder.Configuration);
builder.Services.AddShippingModule(builder.Configuration);
builder.Services.AddReviewModule(builder.Configuration);
builder.Services.AddNotificationModule(builder.Configuration);
builder.Services.AddSearchModule(builder.Configuration);

// 6. OpenAPI Documentation & HealthChecks
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

// 7. Middlewares pipeline
app.UseBuildingBlocks();
app.UseCors("AllowAll");
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// 8. Health Check & Metrics Endpoints (K8s Probes & Prometheus)
app.MapGet("/health/live", () => Results.Ok(new { status = "ALIVE", timestamp = DateTime.UtcNow }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "READY", timestamp = DateTime.UtcNow }));
app.MapMetrics();

// 9. Map Modular Endpoints
app.MapIdentityEndpoints();
app.MapProductEndpoints();
app.MapInventoryEndpoints();
app.MapCartEndpoints();
app.MapOrderEndpoints();
app.MapPaymentEndpoints();
app.MapPromotionEndpoints();
app.MapShippingEndpoints();
app.MapReviewEndpoints();
app.MapNotificationEndpoints();
app.MapSearchEndpoints();

app.Run();
