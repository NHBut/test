using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using ECommerce.BuildingBlocks.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerce.BuildingBlocks.Kafka;

public abstract class KafkaConsumerBase<TMessage> : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly string _topic;
    private readonly string _consumerGroup;

    protected KafkaConsumerBase(
        string topic,
        string consumerGroup,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _topic = topic;
        _consumerGroup = consumerGroup;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected abstract Task HandleMessageAsync(TMessage message, string messageId, IServiceProvider sp, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Không chặn quá trình khởi động ASP.NET Core host

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = _consumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false // Tắt auto commit: Phải commit thủ công sau khi xử lý DB thành công
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_topic);

        _logger.LogInformation("Kafka Consumer started for Topic: {Topic}, Group: {Group}", _topic, _consumerGroup);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = consumer.Consume(TimeSpan.FromSeconds(1));
                if (consumeResult == null || consumeResult.IsPartitionEOF) continue;

                var messageId = GetHeaderValue(consumeResult.Message.Headers, "message-id") 
                                ?? $"{consumeResult.Topic}_{consumeResult.Partition.Value}_{consumeResult.Offset.Value}";

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CommonDbContext>();

                // Idempotency Guard: Kiểm tra xem message đã được xử lý chưa
                var alreadyProcessed = await dbContext.ProcessedEvents
                    .AnyAsync(x => x.EventId == messageId && x.ConsumerGroup == _consumerGroup, stoppingToken);

                if (alreadyProcessed)
                {
                    _logger.LogInformation("Duplicate message detected: {MessageId}. Skipping.", messageId);
                    consumer.Commit(consumeResult);
                    continue;
                }

                // Xử lý message với retry nội bộ 3 lần
                var success = false;
                var retryCount = 0;
                Exception? lastException = null;

                while (retryCount < 3 && !success && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var typedMessage = JsonSerializer.Deserialize<TMessage>(consumeResult.Message.Value);
                        if (typedMessage != null)
                        {
                            await HandleMessageAsync(typedMessage, messageId, scope.ServiceProvider, stoppingToken);
                        }

                        // Đánh dấu đã xử lý
                        dbContext.ProcessedEvents.Add(new ProcessedEventRecord
                        {
                            EventId = messageId,
                            ConsumerGroup = _consumerGroup,
                            ProcessedAt = DateTime.UtcNow
                        });
                        await dbContext.SaveChangesAsync(stoppingToken);

                        success = true;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        lastException = ex;
                        _logger.LogWarning(ex, "Error processing Kafka message {MessageId} from {Topic}. Retry attempt: {Count}", messageId, _topic, retryCount);
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, retryCount)), stoppingToken);
                    }
                }

                if (success)
                {
                    consumer.Commit(consumeResult);
                }
                else
                {
                    // Quá 3 lần retry: Đẩy sang Dead Letter Topic (DLT)
                    _logger.LogError(lastException, "Message {MessageId} failed after 3 retries. Moving to DLT.", messageId);
                    var producer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();
                    var dltHeaders = new Dictionary<string, string>
                    {
                        { "x-original-topic", _topic },
                        { "x-error-message", lastException?.Message ?? "Unknown error" }
                    };
                    await producer.ProduceAsync($"{_topic}-dlt", consumeResult.Message.Key, consumeResult.Message.Value, dltHeaders, stoppingToken);
                    consumer.Commit(consumeResult); // Commit để không block partition
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Kafka consumer for {Topic}", _topic);
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }

    private static string? GetHeaderValue(Headers headers, string key)
    {
        if (headers.TryGetLastBytes(key, out var bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }
        return null;
    }
}
