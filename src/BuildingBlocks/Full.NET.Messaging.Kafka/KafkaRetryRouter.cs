using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 将瞬态失败路由到静态 Retry Topic（后缀 .retry.{stage}）。
/// </summary>
internal sealed class KafkaRetryRouter
{
    private readonly KafkaMessagingOptions _options;
    private readonly KafkaMessagingProducer _producer;
    private readonly ILogger<KafkaRetryRouter> _logger;

    public KafkaRetryRouter(
        IOptions<KafkaMessagingOptions> options,
        KafkaMessagingProducer producer,
        ILogger<KafkaRetryRouter> logger)
    {
        _options = options.Value;
        _producer = producer;
        _logger = logger;
    }

    public string? GetNextRetryTopic(string sourceTopic, int attemptCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTopic);
        if (attemptCount < 0 || attemptCount >= _options.RetryStages.Length)
        {
            return null;
        }

        var baseTopic = KafkaTopicNames.ResolveBaseTopic(sourceTopic);
        return KafkaTopicNames.GetRetryTopic(baseTopic, _options.RetryStages[attemptCount]);
    }

    public async Task<bool> TryRouteAsync(
        ConsumeResult<string, byte[]> consumeResult,
        string consumerName,
        IntegrationEventFailure failure,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(failure);

        var retryTopic = GetNextRetryTopic(consumeResult.Topic, attemptCount);
        if (retryTopic is null)
        {
            return false;
        }

        var failedAtUtc = DateTimeOffset.UtcNow;
        var headers = KafkaDeliveryHeaders.CloneHeaders(consumeResult.Message.Headers);
        KafkaDeliveryHeaders.ApplyFailureMetadata(
            headers,
            consumerName,
            consumeResult,
            failure,
            attemptCount + 1,
            failedAtUtc);
        if (!KafkaRetryStageParser.TryParse(
                _options.RetryStages[attemptCount],
                out var retryDelay))
        {
            throw new InvalidOperationException(
                $"Retry stage '{_options.RetryStages[attemptCount]}' is invalid.");
        }
        KafkaDeliveryHeaders.SetRetryNotBeforeUtc(
            headers,
            failedAtUtc.Add(retryDelay));

        var message = new Message<string, byte[]>
        {
            Key = consumeResult.Message.Key,
            Value = consumeResult.Message.Value,
            Headers = headers,
        };

        var published = await _producer
            .TryProduceAsync(retryTopic, message, cancellationToken)
            .ConfigureAwait(false);
        if (!published)
        {
            _logger.LogWarning(
                "Failed to publish Kafka retry message for consumer {ConsumerName} to topic {RetryTopic} with code {FailureCode}.",
                consumerName,
                retryTopic,
                failure.Code);
        }

        return published;
    }
}

internal static class KafkaRetryStageParser
{
    public static bool TryParse(string? value, out TimeSpan delay)
    {
        delay = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
        {
            return false;
        }

        var unit = char.ToLowerInvariant(value[^1]);
        if (!int.TryParse(
                value.AsSpan(0, value.Length - 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amount)
            || amount <= 0)
        {
            return false;
        }

        try
        {
            delay = unit switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                _ => default,
            };
        }
        catch (OverflowException)
        {
            delay = default;
            return false;
        }

        return delay > TimeSpan.Zero;
    }
}
