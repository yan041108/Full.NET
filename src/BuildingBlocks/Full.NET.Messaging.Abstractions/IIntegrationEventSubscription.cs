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