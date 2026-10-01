using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerce.BuildingBlocks.Kafka;

public interface IKafkaProducer
{
    Task ProduceAsync<T>(string topic, string partitionKey, T message, IDictionary<string, string>? customHeaders = null, CancellationToken ct = default);
}

public class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IConfiguration configuration, ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All, // Đảm bảo độ bền bỉ cao nhất (In-sync replicas confirmed)
            EnableIdempotence = true, // Chống duplicate message tại producer network level
            MessageSendMaxRetries = 5,
            RetryBackoffMs = 200,
            LingerMs = 5 // Tối ưu throughput batching nhỏ
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task ProduceAsync<T>(string topic, string partitionKey, T message, IDictionary<string, string>? customHeaders = null, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message);
        var headers = new Headers();

        // Gắn W3C TraceContext nếu có Activity hiện tại
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            headers.Add("traceparent", System.Text.Encoding.UTF8.GetBytes(currentActivity.Id ?? string.Empty));
        }

        // Gắn MessageId duy nhất phục vụ Idempotent Consumer
        headers.Add("message-id", System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
        headers.Add("timestamp", System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")));

        if (customHeaders != null)
        {
            foreach (var kv in customHeaders)
            {
                headers.Add(kv.Key, System.Text.Encoding.UTF8.GetBytes(kv.Value));
            }
        }

        var kafkaMessage = new Message<string, string>
        {
            Key = partitionKey,
            Value = json,
            Headers = headers
        };

        try
        {
            var deliveryResult = await _producer.ProduceAsync(topic, kafkaMessage, ct);
            _logger.LogInformation("Produced event to {Topic} [Partition: {Partition}, Offset: {Offset}] Key: {Key}", 
                topic, deliveryResult.Partition.Value, deliveryResult.Offset.Value, partitionKey);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to produce event to Kafka topic {Topic} with Key: {Key}", topic, partitionKey);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
