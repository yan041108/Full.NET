namespace Full.NET.Data.Abstractions;

/// <summary>
/// Inbox 批量只读预检输入；PayloadHash 必须是消息正文的 SHA-256。
/// </summary>
public sealed class InboxMessageFingerprint
{
    /// <summary>
    /// 构造 Inbox 消息指纹；参数校验失败时抛出 <see cref="ArgumentException"/>。
    /// </summary>
    /// <param name="messageId">Outbox 分配的事件唯一标识，禁止为 Guid.Empty。</param>
    /// <param name="payloadHash">载荷字节的 SHA-256 哈希，必须恰好 32 字节。</param>
    public InboxMessageFingerprint(Guid messageId, ReadOnlyMemory<byte> payloadHash)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("MessageId must be assigned.", nameof(messageId));
        }

        if (payloadHash.Length != 32)
        {
            throw new ArgumentException("PayloadHash must contain exactly 32 bytes.", nameof(payloadHash));
        }

        MessageId = messageId;
        PayloadHash = payloadHash.ToArray();
    }

    /// <summary>
    /// Outbox 分配的事件唯一标识；Inbox 以 (consumer_name, message_id) 作为联合主键。
    /// </summary>
    public Guid MessageId { get; }

    /// <summary>
    /// 载荷字节的 SHA-256 哈希（32 字节）；用于检测同 MessageId 重复投递时 Payload 是否一致。
    /// </summary>
    public ReadOnlyMemory<byte> PayloadHash { get; }
}

/// <summary>
/// 只读预检分类；Unknown 仍必须进入原有事务 Claim，不能视为已取得处理权。
/// </summary>
public enum InboxPrecheckStatus
{
    /// <summary>
    /// 数据库中不存在对应指纹记录；需要进入 Claim 流程原子声明处理权。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 同 Consumer 下该 MessageId 已完成处理且 PayloadHash 一致；可直接跳过业务 Handler。
    /// </summary>
    AlreadyProcessed = 1,

    /// <summary>
    /// 同 Consumer 下该 MessageId 已存在但 SHA-256 不匹配；属于契约冲突，Dispatcher 会拒绝消费。
    /// </summary>
    PayloadMismatch = 2,
}

/// <summary>
/// 保持输入顺序返回的 Inbox 预检结果。
/// </summary>
public readonly record struct InboxPrecheckResult(
    /// <summary>对应指纹中的 MessageId，用于调用方按输入顺序回填状态。</summary>
    Guid MessageId,
    /// <summary>只读预检分类结果；Unknown 仍需进入 Claim 事务。</summary>
    InboxPrecheckStatus Status);
