using Microsoft.EntityFrameworkCore;

namespace ECommerce.BuildingBlocks.Database;

public class CommonDbContext : DbContext
{
    public CommonDbContext(DbContextOptions<CommonDbContext> options) : base(options)
    {
    }

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();
    public DbSet<ProcessedEventRecord> ProcessedEvents => Set<ProcessedEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("common");

        modelBuilder.Entity<OutboxEvent>(b =>
        {
            b.ToTable("outbox_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.AggregateType).HasMaxLength(100).IsRequired();
            b.Property(x => x.AggregateId).HasMaxLength(100).IsRequired();
            b.Property(x => x.EventType).HasMaxLength(150).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Status).HasMaxLength(30).IsRequired();

            // Partial Index tối ưu polling worker: WHERE status = 'PENDING'
            b.HasIndex(x => x.CreatedAt)
             .HasFilter("\"Status\" = 'PENDING'")
             .HasDatabaseName("idx_outbox_pending_poll");
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_keys");
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasMaxLength(255);
            b.Property(x => x.Endpoint).HasMaxLength(255).IsRequired();
            b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.Status).HasMaxLength(30).IsRequired();
            b.HasIndex(x => x.ExpiresAt).HasDatabaseName("idx_idempotency_expiry");
        });

        modelBuilder.Entity<AuditLogRecord>(b =>
        {
            b.ToTable("audit_logs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(100).IsRequired();
            b.Property(x => x.EntityName).HasMaxLength(100).IsRequired();
            b.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            b.Property(x => x.OldValues).HasColumnType("jsonb");
            b.Property(x => x.NewValues).HasColumnType("jsonb");
            b.HasIndex(x => new { x.EntityName, x.EntityId, x.Timestamp }).HasDatabaseName("idx_audit_entity");
        });

        modelBuilder.Entity<ProcessedEventRecord>(b =>
        {
            b.ToTable("processed_events");
            b.HasKey(x => new { x.EventId, x.ConsumerGroup });
            b.Property(x => x.EventId).HasMaxLength(150);
            b.Property(x => x.ConsumerGroup).HasMaxLength(100);
        });
    }
}
