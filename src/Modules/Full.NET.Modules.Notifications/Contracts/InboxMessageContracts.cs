namespace Full.NET.Modules.Notifications.Contracts;

public static class InboxPermissions
{
    public const string Read = "notifications.inbox.read";

    public const string Write = "notifications.inbox.write";
}

public static class InboxMessageStatuses
{
    public const string Unread = "unread";

    public const string Read = "read";
}

public sealed record InboxMessageResponse(
    Guid Id,
    string Title,
    string Content,
    string Status,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedByUserId);

public sealed record InboxUnreadCountResponse(int UnreadCount);

public sealed record SendHostInboxMessageRequest(
    Guid RecipientUserId,
    string Title,
    string Content);
