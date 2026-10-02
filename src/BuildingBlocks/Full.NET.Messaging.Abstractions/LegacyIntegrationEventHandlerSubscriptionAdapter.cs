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

    /// <summary>
    /// 用旧集成事件处理器构造订阅适配器。
    /// </summary>
    /// <param name="handler">被适配的旧 <see cref="IIntegrationEventHandler"/> 实例。</param>
    public LegacyIntegrationEventHandlerSubscriptionAdapter(IIntegrationEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <summary>
    /// 订阅使用的 ConsumerName；旧 Outbox 轮询 Worker 统一使用 <see cref="LegacyConsumerName"/>。
    /// </summary>
    public string ConsumerName => LegacyConsumerName;

    /// <summary>
    /// 被适配旧 Handler 声明的事件类型稳定键；与信封 MessageType 匹配。
    /// </summary>
    public string EventType => _handler.EventType;

    /// <summary>
    /// 被适配旧 Handler 声明的事件 Schema 版本号。
    /// </summary>
    public int SchemaVersion => _handler.SchemaVersion;

    /// <summary>
    /// 被适配旧 Handler 的幂等策略；决定运行期如何去重与重试。
    /// </summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        _handler.IdempotencyStrategy;

    /// <summary>
    /// 将事件上下文与载荷委托给被适配的旧 Handler 处理；路由与幂等策略仍由目录统一裁决。
    /// </summary>
    /// <param name="context">包含租户、追踪等信息的集成事件上下文。</param>
    /// <param name="payload">事件原始载荷字节。</param>
    /// <param name="cancellationToken">用于取消处理的令牌。</param>
    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        _handler.HandleAsync(context, payload, cancellationToken);

    /// <summary>获取被适配的旧 Handler 实例。</summary>
    public IIntegrationEventHandler Handler => _handler;
}