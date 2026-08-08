using Full.NET.Abstractions.Messaging;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Kafka Consumer 侧的稳定订阅身份与处理契约；路由键为 (ConsumerName, EventType, SchemaVersion)。
/// </summary>
public interface IIntegrationEventSubscription
{
    string ConsumerName { get; }

    string EventType { get; }

    int SchemaVersion { get; }

    IntegrationEventIdempotencyStrategy IdempotencyStrategy { get; }

    Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}