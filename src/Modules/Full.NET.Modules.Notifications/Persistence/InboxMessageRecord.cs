namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>
/// 站内信表行的 Dapper 投影模型，列名与 PascalCase 列直接映射。
/// </summary>
internal sealed class InboxMessageRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid RecipientUserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? ReadAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
}
