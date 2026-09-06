using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>通知 Intent 附件投影 SQL。</summary>
internal static class NotificationIntentAttachmentSql
{
    public static readonly SqlStatement Insert = new(
        "notifications.intent_attachment.insert",
        """
        INSERT INTO fn_notifications_intent_attachment
            (Id, IntentId, FileId, SortOrder, CreatedAtUtc)
        VALUES
            (@Id, @IntentId, @FileId, @SortOrder, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListByIntent = new(
        "notifications.intent_attachment.list_by_intent",
        """
        SELECT Id, IntentId, FileId, SortOrder, CreatedAtUtc
        FROM fn_notifications_intent_attachment
        WHERE IntentId = @IntentId
        ORDER BY SortOrder, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ExistsForProbe = new(
        "notifications.intent_attachment.exists_for_probe",
        """
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM fn_notifications_intent_attachment
            WHERE IntentId = @IntentId
              AND FileId = @FileId
        ) THEN 1 ELSE 0 END
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountPendingDeliveriesByIntent = new(
        "notifications.intent_attachment.count_pending_deliveries",
        """
        SELECT COUNT(1)
        FROM fn_notifications_delivery
        WHERE IntentId = @IntentId
          AND StatusKey = 'accepted'
        """,
        SqlDataScope.Global);
}

/// <summary>Intent 附件投影记录。</summary>
internal sealed record NotificationIntentAttachmentRecord(
    Guid Id,
    Guid IntentId,
    Guid FileId,
    int SortOrder,
    DateTimeOffset CreatedAtUtc);
