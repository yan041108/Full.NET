using Full.NET.Messaging.Abstractions;

namespace Full.NET.Data.Abstractions;

/// <summary>
/// 消费侧 Inbox 幂等存储；所有写入必须在调用方已打开的本地命令事务内执行。
/// </summary>
public interface IIntegrationEventInbox
{
    /// <summary>
    /// 一次只读查询预检最多 100 条消息；该结果不是锁、租约或事务 Claim。
    /// </summary>
    Task<IReadOnlyList<InboxPrecheckResult>> PrecheckBatchAsync(
        string consumerName,
        IReadOnlyList<InboxMessageFingerprint> messages,
        CancellationToken cancellationToken);

    /// <summary>
    /// 在当前事务内声明 <paramref name="consumerName"/> 对 Envelope 的处理权。
    /// </summary>
    /// <remarks>
    /// 已 processed 且 PayloadHash 一致时返回 <see cref="InboxClaimStatus.AlreadyProcessed"/>；
    /// 同 MessageId 不同 SHA-256 时返回 <see cref="InboxClaimStatus.PayloadMismatch"/>。
    /// </remarks>
    Task<InboxClaimResult> ClaimAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken);

    /// <summary>
    /// 将已声明的处理行标记为 processed；要求当前状态为 processing。
    /// </summary>
    Task MarkProcessedAsync(
        string consumerName,
        Guid messageId,
        CancellationToken cancellationToken);
}
