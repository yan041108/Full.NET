namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>
/// Host 公告表行的 Dapper 投影模型，列名与 PascalCase 列直接映射。
/// </summary>
internal sealed class AnnouncementRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public int Version { get; set; }
}
