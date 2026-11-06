using Full.NET.Abstractions.Messaging;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Kafka Consumer 侧的稳定订阅身份与处理契约；路由键为 (ConsumerName, EventType, SchemaVersion)。
/// </summary>
public interface IIntegrationEventSubscription
{
    /// <summary>消费方稳定名称，与事件类型、模式版本共同构成路由键；发布方不应假设其取值。</summary>
    string ConsumerName { get; }

    /// <summary>订阅的事件稳定类型名，参与路由键匹配。</summary>
    string EventType { get; }

    /// <summary>载荷模式版本，参与路由键并决定反序列化方式；升级须保留旧版本处理器。</summary>
    int SchemaVersion { get; }

    /// <summary>该订阅采取的去重策略；实现须据此保证至少一次投递下的业务幂等。</summary>
    IntegrationEventIdempotencyStrategy IdempotencyStrategy { get; }

    /// <summary>
    /// 处理一条集成事件；实现必须按 <see cref="IdempotencyStrategy"/> 保证至少一次投递下的业务幂等。
    /// </summary>
    /// <param name="context">集成事件上下文，包含 MessageId、租户、追踪与发生时间等稳定元数据，供去重与租户边界判断。</param>
    /// <param name="payload">事件序列化载荷的只读内存视图；消费者按 <see cref="SchemaVersion"/> 反序列化，不得修改底层缓冲区。</param>
    /// <param name="cancellationToken">用于取消事件处理的令牌。</param>
    Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}