using ECommerce.BuildingBlocks.CQRS;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Promotion;

public class Coupon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string DiscountType { get; set; } = "PERCENTAGE"; // PERCENTAGE, FIXED_AMOUNT
    public decimal DiscountValue { get; set; }
    public decimal MinOrderValue { get; set; } = 0;
    public decimal? MaxDiscountAmount { get; set; }
    public int UsageLimit { get; set; } = 1000;
    public int UsageCount { get; set; } = 0;
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    public decimal CalculateDiscount(decimal subtotal)
    {
        if (!IsActive || DateTime.UtcNow > ExpiresAt || UsageCount >= UsageLimit) return 0;
        if (subtotal < MinOrderValue) return 0;

        decimal discount = 0;
        if (DiscountType == "PERCENTAGE")
        {
            discount = subtotal * (DiscountValue / 100m);
            if (MaxDiscountAmount.HasValue && discount > MaxDiscountAmount.Value)
            {
                discount = MaxDiscountAmount.Value;
            }
        }
        else
        {
            discount = DiscountValue;
        }

        return Math.Min(discount, subtotal);
    }
}

public class PromotionDbContext : DbContext
{
    public PromotionDbContext(DbContextOptions<PromotionDbContext> options) : base(options) { }
    public DbSet<Coupon> Coupons => Set<Coupon>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("promotion");

        modelBuilder.Entity<Coupon>(b =>
        {
            b.ToTable("coupons");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.DiscountValue).HasPrecision(15, 2);
            b.Property(x => x.MinOrderValue).HasPrecision(15, 2);
            b.Property(x => x.MaxDiscountAmount).HasPrecision(15, 2);
        });
    }
}

public record ApplyCouponCommand(string Code, decimal Subtotal) : ICommand<ApplyCouponResult>;
public record ApplyCouponResult(bool IsValid, decimal DiscountAmount, string Message);

public class ApplyCouponHandler : ICommandHandler<ApplyCouponCommand, ApplyCouponResult>
{
    private readonly PromotionDbContext _db;

    public ApplyCouponHandler(PromotionDbContext db) => _db = db;

    public async Task<ApplyCouponResult> Handle(ApplyCouponCommand request, CancellationToken cancellationToken)
    {
        var coupon = await _db.Coupons.FirstOrDefaultAsync(x => x.Code == request.Code.Trim().ToUpperInvariant(), cancellationToken);
        if (coupon == null) return new ApplyCouponResult(false, 0, "Coupon not found.");

        var discount = coupon.CalculateDiscount(request.Subtotal);
        if (discount <= 0) return new ApplyCouponResult(false, 0, "Coupon is not applicable or minimum order amount not met.");

        return new ApplyCouponResult(true, discount, "Coupon applied successfully.");
    }
}

public static class PromotionModuleExtensions
{
    public static IServiceCollection AddPromotionModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<PromotionDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "promotion");
            });
        });

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PromotionModuleExtensions).Assembly));
        return services;
    }

    public static IEndpointRouteBuilder MapPromotionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/promotions").WithTags("Promotion");

        group.MapPost("/apply", async (ApplyCouponCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsValid ? Results.Ok(result) : Results.BadRequest(result);
        }).WithName("ApplyCoupon");

        return app;
    }
}
