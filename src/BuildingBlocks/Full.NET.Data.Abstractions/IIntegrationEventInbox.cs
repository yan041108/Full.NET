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
    /// <param name="consumerName">当前消费者的稳定名称（通常是服务名 + 订阅组）。</param>
    /// <param name="messages">待预检的消息指纹集合，数量不超过 100。</param>
    /// <param name="cancellationToken">用于取消只读查询的令牌。</param>
    /// <returns>与输入顺序一一对应的预检结果列表。</returns>
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
    /// <param name="consumerName">当前消费者的稳定名称。</param>
    /// <param name="envelope">包含 MessageId 与 PayloadHash 的事件信封。</param>
    /// <param name="cancellationToken">用于取消声明操作的令牌。</param>
    /// <returns>声明结果：Claimed / AlreadyProcessed / PayloadMismatch。</returns>
    Task<InboxClaimResult> ClaimAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken);

    /// <summary>
    /// 将已声明的处理行标记为 processed；要求当前状态为 processing。
    /// </summary>
    /// <param name="consumerName">当前消费者的稳定名称。</param>
    /// <param name="messageId">需要闭环的事件标识。</param>
    /// <param name="cancellationToken">用于取消更新操作的令牌。</param>
    Task MarkProcessedAsync(
        string consumerName,
        Guid messageId,
        CancellationToken cancellationToken);
}
