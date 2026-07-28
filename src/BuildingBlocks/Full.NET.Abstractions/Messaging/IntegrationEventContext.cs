namespace Full.NET.Abstractions.Messaging;

/// <summary>
/// 描述一次 Outbox 投递的稳定消息上下文，供消费者实现去重、追踪和租户边界判断。
/// </summary>
/// <param name="MessageId">持久化 Outbox 消息的稳定标识；重试与租约回收时保持不变。</param>
/// <param name="MessageType">当前持久化记录使用的规范或兼容消息类型。</param>
/// <param name="SchemaVersion">消息载荷的模式版本。</param>
/// <param name="TenantId">消息所属租户；Host 级消息为空。</param>
/// <param name="TraceId">生产消息时捕获的追踪标识；不可用时为空。</param>
/// <param name="OccurredAtUtc">业务事件发生并写入 Outbox 的 UTC 时间。</param>
public sealed record IntegrationEventContext(
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    Guid? TenantId,
    string? TraceId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// 声明 Integration Event Handler 在至少一次投递下采用的幂等策略。
/// </summary>
public enum IntegrationEventIdempotencyStrategy
{
    /// <summary>
    /// 尚未完成幂等性评估；Worker 启动校验必须拒绝该值。
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// 重复执行只会收敛到相同业务状态，不需要持久化 MessageId。
    /// </summary>
    NaturallyIdempotent = 1,

    /// <summary>
    /// Handler 使用稳定 MessageId 在副作用提交边界持久化去重。
    /// </summary>
    MessageIdDeduplication = 2,
}
