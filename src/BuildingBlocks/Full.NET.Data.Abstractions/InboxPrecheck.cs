namespace Full.NET.Data.Abstractions;

/// <summary>
/// Inbox 批量只读预检输入；PayloadHash 必须是消息正文的 SHA-256。
/// </summary>
public sealed class InboxMessageFingerprint
{
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

    public Guid MessageId { get; }

    public ReadOnlyMemory<byte> PayloadHash { get; }
}

/// <summary>
/// 只读预检分类；Unknown 仍必须进入原有事务 Claim，不能视为已取得处理权。
/// </summary>
public enum InboxPrecheckStatus
{
    Unknown = 0,
    AlreadyProcessed = 1,
    PayloadMismatch = 2,
}

/// <summary>
/// 保持输入顺序返回的 Inbox 预检结果。
/// </summary>
public readonly record struct InboxPrecheckResult(
    Guid MessageId,
    InboxPrecheckStatus Status);
