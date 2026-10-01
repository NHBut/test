using ECommerce.BuildingBlocks.CQRS;
using ECommerce.BuildingBlocks.Database;
using ECommerce.BuildingBlocks.Outbox;
using ECommerce.BuildingBlocks.Redis;
using ECommerce.Modules.Product.Domain;
using ECommerce.Modules.Product.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Modules.Product.Application;

public record CreateVariantDto(string Sku, string VariantName, decimal Price, decimal? CompareAtPrice, string AttributesJson);

public record CreateProductCommand(string Name, string Slug, string? Brand, string? Description, Guid? CategoryId, List<CreateVariantDto> Variants) : ICommand<Guid>;

public record ProductDto(Guid Id, string Name, string Slug, string? Brand, string? Description, Guid? CategoryId, List<ProductVariantDto> Variants);

public record ProductVariantDto(Guid Id, string Sku, string VariantName, decimal Price, decimal? CompareAtPrice, string AttributesJson);

public record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto?>;

public record GetProductsQuery(int Page = 1, int PageSize = 20, Guid? CategoryId = null) : IQuery<List<ProductDto>>;

public class ProductHandlers :
    ICommandHandler<CreateProductCommand, Guid>,
    IQueryHandler<GetProductByIdQuery, ProductDto?>,
    IQueryHandler<GetProductsQuery, List<ProductDto>>
{
    private readonly ProductDbContext _db;
    private readonly CommonDbContext _commonDb;
    private readonly IOutboxService _outbox;
    private readonly IRedisCacheService _redis;

    public ProductHandlers(ProductDbContext db, CommonDbContext commonDb, IOutboxService outbox, IRedisCacheService redis)
    {
        _db = db;
        _commonDb = commonDb;
        _outbox = outbox;
        _redis = redis;
    }

    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = Domain.Product.Create(request.Name, request.Slug, request.Brand, request.Description, request.CategoryId);

        foreach (var v in request.Variants)
        {
            product.AddVariant(v.Sku, v.VariantName, v.Price, v.CompareAtPrice, v.AttributesJson);
        }

        _db.Products.Add(product);
        // Lưu Outbox Event vào common schema trong cùng một transaction
        _outbox.EnqueueDomainEvent(product, _commonDb);

        await _db.SaveChangesAsync(cancellationToken);
        await _commonDb.SaveChangesAsync(cancellationToken);

        return product.Id;
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"product:id:{request.ProductId}";

        // Áp dụng Cache Aside Pattern với Redis
        return await _redis.GetOrSetAsync(cacheKey, async () =>
        {
            var p = await _db.Products
                .Include(x => x.Variants)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.ProductId, cancellationToken);

            if (p == null) return null;

            return new ProductDto(
                p.Id,
                p.Name,
                p.Slug,
                p.Brand,
                p.Description,
                p.CategoryId,
                p.Variants.Select(v => new ProductVariantDto(v.Id, v.Sku, v.VariantName, v.Price, v.CompareAtPrice, v.AttributesJson)).ToList()
            );
        }, TimeSpan.FromHours(1), cancellationToken);
    }

    public async Task<List<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Products.Include(x => x.Variants).AsNoTracking().AsQueryable();

        if (request.CategoryId.HasValue)
        {
            query = query.Where(x => x.CategoryId == request.CategoryId.Value);
        }

        var products = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return products.Select(p => new ProductDto(
            p.Id,
            p.Name,
            p.Slug,
            p.Brand,
            p.Description,
            p.CategoryId,
            p.Variants.Select(v => new ProductVariantDto(v.Id, v.Sku, v.VariantName, v.Price, v.CompareAtPrice, v.AttributesJson)).ToList()
        )).ToList();
    }
}
