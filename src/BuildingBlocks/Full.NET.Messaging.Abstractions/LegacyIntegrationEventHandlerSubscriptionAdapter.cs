using Full.NET.Abstractions.Messaging;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 将旧 <see cref="IIntegrationEventHandler"/> 适配为 Kafka 订阅契约，供目录统一校验与路由。
/// </summary>
/// <remarks>
/// 适配器只暴露规范 <see cref="IIntegrationEventHandler.EventType"/>；历史别名仍由
/// <see cref="IntegrationEventHandlerMatcher"/> 在旧轮询路径解析。
/// </remarks>
public sealed class LegacyIntegrationEventHandlerSubscriptionAdapter : IIntegrationEventSubscription
{
    /// <summary>旧 Outbox 轮询 Worker 使用的稳定 ConsumerName。</summary>
    public const string LegacyConsumerName = "fullnet.worker.legacy-polling";

    private readonly IIntegrationEventHandler _handler;

    public LegacyIntegrationEventHandlerSubscriptionAdapter(IIntegrationEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public string ConsumerName => LegacyConsumerName;

    public string EventType => _handler.EventType;

    public int SchemaVersion => _handler.SchemaVersion;

    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        _handler.IdempotencyStrategy;

    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        _handler.HandleAsync(context, payload, cancellationToken);

    /// <summary>获取被适配的旧 Handler 实例。</summary>
    public IIntegrationEventHandler Handler => _handler;
}