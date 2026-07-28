namespace Full.NET.Data.Abstractions;

/// <summary>
/// 定义 Outbox 成功终态消息的小批量保留清理边界。
/// </summary>
/// <remarks>
/// 只有处理成功且早于截止时间的消息可以被删除；待处理、重试、租约中和死信消息必须保留。
/// </remarks>
public interface IOutboxRetentionStore
{
    /// <summary>
    /// 删除一批严格早于截止时间的成功终态消息。
    /// </summary>
    /// <param name="cutoffUtc">成功处理时间的排他上界。</param>
    /// <param name="batchSize">单批允许删除的最大行数。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>本批实际删除的行数。</returns>
    Task<int> DeleteProcessedBatchAsync(
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken);
}
