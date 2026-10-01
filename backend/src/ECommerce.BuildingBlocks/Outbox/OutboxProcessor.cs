using System.Text.Json;
using ECommerce.BuildingBlocks.Database;
using ECommerce.BuildingBlocks.Domain;
using ECommerce.BuildingBlocks.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerce.BuildingBlocks.Outbox;

public interface IOutboxService
{
    void EnqueueDomainEvent<TId>(AggregateRoot<TId> aggregate, CommonDbContext dbContext);
}

public class OutboxService : IOutboxService
{
    public void EnqueueDomainEvent<TId>(AggregateRoot<TId> aggregate, CommonDbContext dbContext)
    {
        foreach (var @event in aggregate.DomainEvents)
        {
            var eventType = @event.GetType().Name;
            var payload = JsonSerializer.Serialize(@event, @event.GetType());
            
            var outboxEvent = new OutboxEvent
            {
                AggregateType = aggregate.GetType().Name,
                AggregateId = aggregate.Id?.ToString() ?? string.Empty,
                EventType = eventType,
                Payload = payload,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };

            dbContext.OutboxEvents.Add(outboxEvent);
        }

        aggregate.ClearDomainEvents();
    }
}

public class OutboxBackgroundProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxBackgroundProcessor> _logger;

    public OutboxBackgroundProcessor(IServiceProvider serviceProvider, ILogger<OutboxBackgroundProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _logger.LogInformation("OutboxBackgroundProcessor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CommonDbContext>();
                var kafkaProducer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();

                // Dùng SQL nguyên tử FOR UPDATE SKIP LOCKED
                var pendingEvents = await dbContext.OutboxEvents
                    .FromSqlRaw(@"
                        SELECT * FROM common.outbox_events 
                        WHERE ""Status"" = 'PENDING' 
                        ORDER BY ""CreatedAt"" ASC 
                        LIMIT 50 
                        FOR UPDATE SKIP LOCKED")
                    .ToListAsync(stoppingToken);

                if (pendingEvents.Count > 0)
                {
                    foreach (var evt in pendingEvents)
                    {
                        try
                        {
                            var topic = MapEventTypeToTopic(evt.EventType);
                            var partitionKey = evt.AggregateId;

                            await kafkaProducer.ProduceAsync(topic, partitionKey, evt.Payload, null, stoppingToken);

                            evt.Status = "PROCESSED";
                            evt.ProcessedAt = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            evt.RetryCount++;
                            evt.ErrorMessage = ex.Message;
                            if (evt.RetryCount >= 5)
                            {
                                evt.Status = "FAILED";
                            }
                            _logger.LogError(ex, "Failed to publish outbox event {EventId}", evt.Id);
                        }
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                else
                {
                    // Nếu không có event pending, nghỉ 200ms
                    await Task.Delay(200, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in OutboxBackgroundProcessor");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private static string MapEventTypeToTopic(string eventType)
    {
        if (eventType.Contains("Order")) return "order-events";
        if (eventType.Contains("Payment")) return "payment-events";
        if (eventType.Contains("Inventory") || eventType.Contains("Stock")) return "inventory-events";
        if (eventType.Contains("Product")) return "product-events";
        if (eventType.Contains("Notification") || eventType.Contains("Email")) return "notification-events";
        return "general-events";
    }
}
