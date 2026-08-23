namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// Notifications 模块稳定错误码集合，作为机器契约不可本地化。
/// </summary>
public static class NotificationsErrorCodes
{
    /// <summary>Notifications 错误码前缀。</summary>
    public const string Prefix = "notifications.";

    /// <summary>公告未找到。</summary>
    public const string AnnouncementNotFound = "notifications.announcement_not_found";

    /// <summary>公告乐观版本号或状态不符，CAS 并发控制失败。</summary>
    public const string AnnouncementConcurrencyConflict = "notifications.announcement_concurrency_conflict";

    /// <summary>公告状态不允许当前操作（仅草稿可更新或发布）。</summary>
    public const string AnnouncementInvalidStatus = "notifications.announcement_invalid_status";

    /// <summary>公告标题或正文长度校验失败。</summary>
    public const string AnnouncementValidationFailed = "notifications.announcement_validation_failed";

    /// <summary>站内信未找到。</summary>
    public const string InboxMessageNotFound = "notifications.inbox_message_not_found";

    /// <summary>站内信收件人不存在或非活动 Host 用户。</summary>
    public const string InboxRecipientNotFound = "notifications.inbox_recipient_not_found";

    /// <summary>站内信标题或正文长度校验失败。</summary>
    public const string InboxValidationFailed = "notifications.inbox_validation_failed";

    /// <summary>已登记的全部错误码，供治理与多语言资源完整性校验使用。</summary>
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
