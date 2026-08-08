using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 将永久失败或耗尽重试的消息发布到 .dlq Topic；日志禁止输出 Payload 或堆栈。
/// </summary>
internal sealed class KafkaDeadLetterPublisher
{
    private readonly KafkaMessagingOptions _options;
    private readonly KafkaMessagingProducer _producer;
    private readonly ILogger<KafkaDeadLetterPublisher> _logger;

    public KafkaDeadLetterPublisher(
        IOptions<KafkaMessagingOptions> options,
        KafkaMessagingProducer producer,
        ILogger<KafkaDeadLetterPublisher> logger)
    {
        _options = options.Value;
        _producer = producer;
        _logger = logger;
    }

    public async Task<bool> TryPublishAsync(
        ConsumeResult<string, byte[]> consumeResult,
        string consumerName,
        IntegrationEventFailure failure,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(failure);

        var baseTopic = KafkaTopicNames.ResolveBaseTopic(consumeResult.Topic);
        var deadLetterTopic = KafkaTopicNames.GetDeadLetterTopic(baseTopic);
        var failedAtUtc = DateTimeOffset.UtcNow;
        var headers = KafkaDeliveryHeaders.CloneHeaders(consumeResult.Message.Headers);
        KafkaDeliveryHeaders.ApplyFailureMetadata(
            headers,
            consumerName,
            consumeResult,
            failure,
            attemptCount,
            failedAtUtc);

        var message = new Message<string, byte[]>
        {
            Key = consumeResult.Message.Key,
            Value = consumeResult.Message.Value,
            Headers = headers,
        };

        var published = await _producer
            .TryProduceAsync(deadLetterTopic, message, cancellationToken)
            .ConfigureAwait(false);
        if (!published)
        {
            _logger.LogWarning(
                "Failed to publish Kafka dead-letter message for consumer {ConsumerName} to topic {DeadLetterTopic} with code {FailureCode}.",
                consumerName,
                deadLetterTopic,
                failure.Code);
        }

        return published;
    }
}
