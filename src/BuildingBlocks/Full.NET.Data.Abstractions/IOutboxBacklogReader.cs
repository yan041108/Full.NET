namespace Full.NET.Data.Abstractions;

/// <summary>
/// 提供不改变租约和消息状态的 Outbox 积压只读快照。
/// </summary>
public interface IOutboxBacklogReader
{
    /// <summary>
    /// 读取当前全部未处理且未死信消息的积压快照。
    /// </summary>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>待处理数量与最老消息发生时间；空队列的时间为空。</returns>
    Task<OutboxBacklogSnapshot> ReadBacklogAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// 按消息类型集合和结构版本读取仍会阻断旧 Handler 退役的消息快照。
    /// </summary>
    /// <param name="messageTypes">同一 Handler 当前声明的 canonical 与 legacy 消息类型。</param>
    /// <param name="schemaVersion">准备退役的正整数结构版本。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>未处理的待消费、死信数量与其中最老消息的发生时间。</returns>
    Task<OutboxVersionRetirementSnapshot> ReadVersionRetirementAsync(
        IReadOnlyCollection<string> messageTypes,
        int schemaVersion,
        CancellationToken cancellationToken);
}

/// <summary>
/// 表示某一采样时刻的 Outbox 未处理消息积压。
/// </summary>
/// <param name="PendingCount">尚未处理且未进入死信终态的消息总数。</param>
/// <param name="OldestOccurredAtUtc">最老待处理消息的 UTC 发生时间；空队列时为空。</param>
public sealed record OutboxBacklogSnapshot(
    long PendingCount,
    DateTimeOffset? OldestOccurredAtUtc);

/// <summary>
/// 表示某个 Handler 版本仍未排空的只读证据。
/// </summary>
/// <param name="PendingCount">尚未处理且未进入死信终态的目标消息数量。</param>
/// <param name="DeadLetterCount">尚未处理且已进入死信终态的目标消息数量。</param>
/// <param name="OldestUnprocessedOccurredAtUtc">全部未处理目标消息中最老的 UTC 发生时间。</param>
public sealed record OutboxVersionRetirementSnapshot(
    long PendingCount,
    long DeadLetterCount,
    DateTimeOffset? OldestUnprocessedOccurredAtUtc);
