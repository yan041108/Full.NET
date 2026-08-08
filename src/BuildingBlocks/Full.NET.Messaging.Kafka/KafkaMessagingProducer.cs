using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 共享 Kafka Producer；Retry/DLQ 发布使用同一 ProducerConfig。
/// </summary>
internal sealed class KafkaMessagingProducer : IDisposable
{
    private readonly Lazy<IProducer<string, byte[]>> _producer;

    public KafkaMessagingProducer(IOptions<KafkaMessagingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var producerConfig = options.Value.BuildProducerConfig();
        _producer = new Lazy<IProducer<string, byte[]>>(
            () => new ProducerBuilder<string, byte[]>(producerConfig).Build());
    }

    public async Task<bool> TryProduceAsync(
        string topic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var result = await _producer.Value
                .ProduceAsync(topic, message, cancellationToken)
                .ConfigureAwait(false);
            return result.Status == PersistenceStatus.Persisted;
        }
        catch (ProduceException<string, byte[]>)
        {
            return false;
        }
        catch (KafkaException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_producer.IsValueCreated)
        {
            _producer.Value.Dispose();
        }
    }
}
