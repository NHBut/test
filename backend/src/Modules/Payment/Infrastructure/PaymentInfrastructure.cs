using System.Security.Cryptography;
using System.Text;
using ECommerce.Modules.Payment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ECommerce.Modules.Payment.Infrastructure;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    public DbSet<Domain.Payment> Payments => Set<Domain.Payment>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("payment");

        modelBuilder.Entity<Domain.Payment>(b =>
        {
            b.ToTable("payments");
            b.HasKey(x => x.Id);
            b.Property(x => x.OrderId).IsRequired();
            b.HasIndex(x => x.OrderId).IsUnique();
            b.Property(x => x.Amount).HasPrecision(15, 2);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            b.Property(x => x.PaymentMethod).HasMaxLength(50).IsRequired();

            b.HasMany(x => x.Transactions).WithOne().HasForeignKey(t => t.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentTransaction>(b =>
        {
            b.ToTable("payment_transactions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Gateway).HasMaxLength(50).IsRequired();
            b.Property(x => x.TransactionCode).HasMaxLength(100).IsRequired();
            b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            b.Property(x => x.Amount).HasPrecision(15, 2);
            b.Property(x => x.RawResponseJson).HasColumnType("jsonb");

            // Unique constraint chống duplicate webhook
            b.HasIndex(x => new { x.Gateway, x.TransactionCode }).IsUnique().HasDatabaseName("uq_gateway_tx");
        });
    }
}

public interface IPaymentGatewayService
{
    string CreatePaymentUrl(Guid paymentId, Guid orderId, decimal amount, string returnUrl);
    bool VerifySignature(string rawData, string signature, string secretKey);
}

public class MockPaymentGatewayService : IPaymentGatewayService
{
    private readonly IConfiguration _config;

    public MockPaymentGatewayService(IConfiguration config) => _config = config;

    public string CreatePaymentUrl(Guid paymentId, Guid orderId, decimal amount, string returnUrl)
    {
        var secret = _config["Payment:GatewaySecret"] ?? "MOCK_PAYMENT_SECRET_KEY_123456789";
        var rawData = $"amount={amount}&orderId={orderId}&paymentId={paymentId}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData)));

        return $"https://mock-gateway.ecommerce.local/checkout?{rawData}&signature={signature}&returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    public bool VerifySignature(string rawData, string signature, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var computedHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData)));

        // Fixed-time comparison chống Timing Attack
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant())
        );
    }
}
