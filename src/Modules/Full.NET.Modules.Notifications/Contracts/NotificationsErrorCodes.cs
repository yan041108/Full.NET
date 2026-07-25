namespace Full.NET.Modules.Notifications.Contracts;

public static class NotificationsErrorCodes
{
    /// <summary>Notifications 错误码前缀。</summary>
    public const string Prefix = "notifications.";

    public const string AnnouncementNotFound = "notifications.announcement_not_found";

    public const string AnnouncementConcurrencyConflict = "notifications.announcement_concurrency_conflict";

    public const string AnnouncementInvalidStatus = "notifications.announcement_invalid_status";

    public const string AnnouncementValidationFailed = "notifications.announcement_validation_failed";

    public const string InboxMessageNotFound = "notifications.inbox_message_not_found";

    public const string InboxRecipientNotFound = "notifications.inbox_recipient_not_found";

    public const string InboxValidationFailed = "notifications.inbox_validation_failed";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        AnnouncementNotFound,
        AnnouncementConcurrencyConflict,
        AnnouncementInvalidStatus,
        AnnouncementValidationFailed,
        InboxMessageNotFound,
        InboxRecipientNotFound,
        InboxValidationFailed,
    ]);
}
