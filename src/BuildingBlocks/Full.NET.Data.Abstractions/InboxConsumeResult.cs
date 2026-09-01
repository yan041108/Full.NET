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
public sealed record InboxConsumeResult(
    /// <summary>当前消费的稳定状态；用于决定是否确认 Broker Offset。</summary>
    InboxConsumeStatus Status)
{
    /// <summary>首次处理完成并已写入 processed 终态。</summary>
    public static InboxConsumeResult Processed { get; } =
        new(InboxConsumeStatus.Processed);

    /// <summary>重复投递，数据库已存在 processed 记录，无需再次执行业务 Handler。</summary>
    public static InboxConsumeResult AlreadyProcessed { get; } =
        new(InboxConsumeStatus.AlreadyProcessed);
}

/// <summary>
/// Inbox 声明处理权时的内部结果；Payload 不一致属于永久契约失败，由 Dispatcher 转为异常。
/// </summary>
public enum InboxClaimStatus
{
    /// <summary>当前事务内成功声明处理权，行状态为 processing，需后续 MarkProcessedAsync 闭环。</summary>
    Claimed = 0,

    /// <summary>同 Consumer 已存在 processed 行且 PayloadHash 一致，直接跳过业务执行。</summary>
    AlreadyProcessed = 1,

    /// <summary>同 Consumer 存在已处理行但 PayloadHash 不一致；Dispatcher 会抛出契约冲突异常。</summary>
    PayloadMismatch = 2,
}

/// <summary>
/// 当前命令事务内 Inbox 声明结果。
/// </summary>
public sealed record InboxClaimResult(
    /// <summary>声明结果枚举；Claimed 时必须在同一事务完成业务写与 MarkProcessedAsync。</summary>
    InboxClaimStatus Status);