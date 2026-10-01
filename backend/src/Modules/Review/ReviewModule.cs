using ECommerce.BuildingBlocks.CQRS;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Review;

public class ProductReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public int Rating { get; set; } // 1 - 5 stars
    public string? Comment { get; set; }
    public bool VerifiedPurchase { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReviewDbContext : DbContext
{
    public ReviewDbContext(DbContextOptions<ReviewDbContext> options) : base(options) { }
    public DbSet<ProductReview> Reviews => Set<ProductReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("review");

        modelBuilder.Entity<ProductReview>(b =>
        {
            b.ToTable("reviews");
            b.HasKey(x => x.Id);
            b.Property(x => x.Rating).IsRequired();
            b.HasIndex(x => new { x.ProductId, x.CreatedAt });
            b.HasIndex(x => new { x.UserId, x.OrderId, x.ProductId }).IsUnique(); // 1 user chỉ review 1 lần cho 1 sản phẩm trong 1 đơn hàng
        });
    }
}

public record AddReviewCommand(Guid ProductId, Guid UserId, Guid OrderId, int Rating, string? Comment) : ICommand<ProductReview>;
public record GetProductReviewsQuery(Guid ProductId, int Page = 1, int PageSize = 20) : IQuery<List<ProductReview>>;

public class ReviewHandlers :
    ICommandHandler<AddReviewCommand, ProductReview>,
    IQueryHandler<GetProductReviewsQuery, List<ProductReview>>
{
    private readonly ReviewDbContext _db;

    public ReviewHandlers(ReviewDbContext db) => _db = db;

    public async Task<ProductReview> Handle(AddReviewCommand request, CancellationToken cancellationToken)
    {
        if (request.Rating < 1 || request.Rating > 5)
            throw new ArgumentException("Rating must be between 1 and 5.");

        var review = new ProductReview
        {
            ProductId = request.ProductId,
            UserId = request.UserId,
            OrderId = request.OrderId,
            Rating = request.Rating,
            Comment = request.Comment,
            VerifiedPurchase = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<List<ProductReview>> Handle(GetProductReviewsQuery request, CancellationToken cancellationToken)
    {
        return await _db.Reviews
            .AsNoTracking()
            .Where(x => x.ProductId == request.ProductId)
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
    }
}

public static class ReviewModuleExtensions
{
    public static IServiceCollection AddReviewModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<ReviewDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "review");
            });
        });

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ReviewModuleExtensions).Assembly));
        return services;
    }

    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reviews").WithTags("Reviews");

        group.MapPost("/", async (AddReviewCommand command, ISender sender) =>
        {
            var review = await sender.Send(command);
            return Results.Created($"/api/v1/reviews/{review.Id}", review);
        }).WithName("AddReview");

        group.MapGet("/product/{productId:guid}", async (Guid productId, int? page, int? pageSize, ISender sender) =>
        {
            var reviews = await sender.Send(new GetProductReviewsQuery(productId, page ?? 1, pageSize ?? 20));
            return Results.Ok(reviews);
        }).WithName("GetProductReviews");

        return app;
    }
}
