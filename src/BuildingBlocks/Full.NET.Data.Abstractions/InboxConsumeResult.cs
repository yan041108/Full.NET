namespace Full.NET.Data.Abstractions;

/// <summary>
/// Inbox 消费管道在 Broker 重投场景下的稳定结果语义。
/// </summary>
public enum InboxConsumeStatus
{
    /// <summary>首次在本消费者事务内完成处理并标记 processed。</summary>
    Processed = 0,

    /// <summary>数据库已提交 processed，可安全确认 Offset 而不再执行业务副作用。</summary>
    AlreadyProcessed = 1,
}

/// <summary>
/// <see cref="IIntegrationEventInbox"/> 与 Consumer Dispatcher 的统一消费结果。
/// </summary>
public sealed record InboxConsumeResult(InboxConsumeStatus Status)
{
    public static InboxConsumeResult Processed { get; } =
        new(InboxConsumeStatus.Processed);

    public static InboxConsumeResult AlreadyProcessed { get; } =
        new(InboxConsumeStatus.AlreadyProcessed);
}

/// <summary>
/// Inbox 声明处理权时的内部结果；Payload 不一致属于永久契约失败，由 Dispatcher 转为异常。
/// </summary>
public enum InboxClaimStatus
{
    Claimed = 0,
    AlreadyProcessed = 1,
    PayloadMismatch = 2,
}

/// <summary>
/// 当前命令事务内 Inbox 声明结果。
/// </summary>
public sealed record InboxClaimResult(InboxClaimStatus Status);