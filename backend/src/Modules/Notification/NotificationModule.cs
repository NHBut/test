using ECommerce.BuildingBlocks.Kafka;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ECommerce.Modules.Notification;

public class NotificationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Channel { get; set; } = "EMAIL"; // EMAIL, SMS, PUSH
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Status { get; set; } = "SENT";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options) { }
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("notification");

        modelBuilder.Entity<NotificationRecord>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(255).IsRequired();
            b.HasIndex(x => new { x.UserId, x.SentAt });
        });
    }
}

public class NotificationMessage
{
    public Guid UserId { get; set; }
    public string Channel { get; set; } = "EMAIL";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class NotificationKafkaConsumer : KafkaConsumerBase<NotificationMessage>
{
    private readonly ILogger<NotificationKafkaConsumer> _logger;

    public NotificationKafkaConsumer(IConfiguration config, IServiceProvider sp, ILogger<NotificationKafkaConsumer> logger)
        : base("notification-events", "notification-service-group", config, sp, logger)
    {
        _logger = logger;
    }

    protected override async Task HandleMessageAsync(NotificationMessage message, string messageId, IServiceProvider sp, CancellationToken ct)
    {
        _logger.LogInformation("Processing notification for User {UserId}: [{Channel}] {Title}", message.UserId, message.Channel, message.Title);

        var db = sp.GetRequiredService<NotificationDbContext>();
        db.Notifications.Add(new NotificationRecord
        {
            UserId = message.UserId,
            Channel = message.Channel,
            Title = message.Title,
            Content = message.Content,
            Status = "SENT",
            SentAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }
}

public static class NotificationModuleExtensions
{
    public static IServiceCollection AddNotificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<NotificationDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "notification");
            });
        });

        services.AddHostedService<NotificationKafkaConsumer>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifications");

        group.MapGet("/user/{userId:guid}", async (Guid userId, NotificationDbContext db) =>
        {
            var notes = await db.Notifications
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.SentAt)
                .Take(50)
                .ToListAsync();

            return Results.Ok(notes);
        }).WithName("GetUserNotifications");

        return app;
    }
}
