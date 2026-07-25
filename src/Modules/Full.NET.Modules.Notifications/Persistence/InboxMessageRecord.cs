namespace Full.NET.Modules.Notifications.Persistence;

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
