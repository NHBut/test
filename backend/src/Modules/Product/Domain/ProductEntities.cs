using ECommerce.BuildingBlocks.Domain;

namespace ECommerce.Modules.Product.Domain;

public record ProductCreatedDomainEvent(Guid ProductId, string Name, string Slug) : IDomainEvent;
public record ProductUpdatedDomainEvent(Guid ProductId, string Name) : IDomainEvent;

public class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Brand { get; private set; }
    public string? Description { get; private set; }
    public Guid? CategoryId { get; private set; }
    public bool IsPublished { get; private set; } = false;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private readonly List<ProductVariant> _variants = new();
    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();

    private Product() { }

    public static Product Create(string name, string slug, string? brand, string? description, Guid? categoryId)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Brand = brand?.Trim(),
            Description = description,
            CategoryId = categoryId,
            IsPublished = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        product.AddDomainEvent(new ProductCreatedDomainEvent(product.Id, product.Name, product.Slug));
        return product;
    }

    public ProductVariant AddVariant(string sku, string variantName, decimal price, decimal? compareAtPrice, string attributesJson = "{}")
    {
        if (price < 0) throw new ArgumentException("Price cannot be negative.");
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            ProductId = Id,
            Sku = sku.Trim().ToUpperInvariant(),
            VariantName = variantName.Trim(),
            Price = price,
            CompareAtPrice = compareAtPrice,
            AttributesJson = attributesJson,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _variants.Add(variant);
        return variant;
    }
}

public class ProductVariant
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public string AttributesJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
