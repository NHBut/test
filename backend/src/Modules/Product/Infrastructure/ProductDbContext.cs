using ECommerce.Modules.Product.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Modules.Product.Infrastructure;

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options)
    {
    }

    public DbSet<Domain.Product> Products => Set<Domain.Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("product");

        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("categories");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            b.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<Domain.Product>(b =>
        {
            b.ToTable("products");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(255).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(300).IsRequired();
            b.HasIndex(x => x.Slug).IsUnique();

            b.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductVariant>(b =>
        {
            b.ToTable("product_variants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            b.HasIndex(x => x.Sku).IsUnique();
            b.Property(x => x.Price).HasPrecision(15, 2);
            b.Property(x => x.CompareAtPrice).HasPrecision(15, 2);
            b.Property(x => x.AttributesJson).HasColumnType("jsonb");
        });
    }
}
