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
}

/// <summary>
/// 表示某一采样时刻的 Outbox 未处理消息积压。
/// </summary>
/// <param name="PendingCount">尚未处理且未进入死信终态的消息总数。</param>
/// <param name="OldestOccurredAtUtc">最老待处理消息的 UTC 发生时间；空队列时为空。</param>
public sealed record OutboxBacklogSnapshot(
    long PendingCount,
    DateTimeOffset? OldestOccurredAtUtc);
