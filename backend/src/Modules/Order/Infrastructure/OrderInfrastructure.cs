using ECommerce.Modules.Order.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Modules.Order.Infrastructure;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
    {
    }

    public DbSet<Domain.Order> Orders => Set<Domain.Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("orders");

        modelBuilder.Entity<Domain.Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
            b.HasIndex(x => x.OrderNumber).IsUnique();

            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            b.Property(x => x.Subtotal).HasPrecision(15, 2);
            b.Property(x => x.DiscountAmount).HasPrecision(15, 2);
            b.Property(x => x.ShippingFee).HasPrecision(15, 2);
            b.Property(x => x.TotalAmount).HasPrecision(15, 2);
            b.Property(x => x.ShippingAddressJson).HasColumnType("jsonb");

            // Composite index cho danh sách đơn hàng của khách hàng
            b.HasIndex(x => new { x.CustomerId, x.CreatedAt }).HasDatabaseName("idx_orders_customer_created");

            b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.ToTable("order_items");
            b.HasKey(x => x.Id);
            b.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            b.Property(x => x.ProductName).HasMaxLength(255).IsRequired();
            b.Property(x => x.VariantName).HasMaxLength(150).IsRequired();
            b.Property(x => x.UnitPrice).HasPrecision(15, 2);
            b.Property(x => x.TotalPrice).HasPrecision(15, 2);
        });

        modelBuilder.Entity<OrderStatusHistory>(b =>
        {
            b.ToTable("order_status_history");
            b.HasKey(x => x.Id);
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
        });
    }
}
