using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

internal static class InboxMessageSql
{
    public static readonly SqlStatement HostRecipientExists =
        new(
            "notifications.host_inbox_recipient_exists",
            """
            SELECT 1
            FROM fn_identity_user
            WHERE Id = @RecipientUserId
              AND ScopeKey = 'host'
              AND TenantId IS NULL
              AND IsActive = 1
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert =
        new(
            "notifications.insert_inbox_message",
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status,
                 ReadAtUtc, CreatedAtUtc, CreatedByUserId)
            VALUES
                (@Id, NULL, @RecipientUserId, @Title, @Content, @Status,
                 NULL, @CreatedAtUtc, @CreatedByUserId)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListForRecipientSqlServer =
        new(
            "notifications.list_inbox_messages.sql_server",
            """
            SELECT Id, TenantId, RecipientUserId, Title, Content, Status,
                   ReadAtUtc, CreatedAtUtc, CreatedByUserId
            FROM fn_notifications_inbox_message
            WHERE RecipientUserId = @RecipientUserId
              AND TenantId IS NULL
            ORDER BY CreatedAtUtc DESC, Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListForRecipientMySql =
        new(
            "notifications.list_inbox_messages.mysql",
            """
            SELECT Id, TenantId, RecipientUserId, Title, Content, Status,
                   ReadAtUtc, CreatedAtUtc, CreatedByUserId
            FROM fn_notifications_inbox_message
            WHERE RecipientUserId = @RecipientUserId
              AND TenantId IS NULL
            ORDER BY CreatedAtUtc DESC, Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountForRecipient =
        new(
            "notifications.count_inbox_messages",
            """
            SELECT COUNT(*)
            FROM fn_notifications_inbox_message
            WHERE RecipientUserId = @RecipientUserId
              AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountUnreadForRecipient =
        new(
            "notifications.count_unread_inbox_messages",
            """
            SELECT COUNT(*)
            FROM fn_notifications_inbox_message
            WHERE RecipientUserId = @RecipientUserId
              AND TenantId IS NULL
              AND Status = @UnreadStatus
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindForRecipientById =
        new(
            "notifications.find_inbox_message_for_recipient",
            """
            SELECT Id, TenantId, RecipientUserId, Title, Content, Status,
                   ReadAtUtc, CreatedAtUtc, CreatedByUserId
            FROM fn_notifications_inbox_message
            WHERE Id = @Id
              AND RecipientUserId = @RecipientUserId
              AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkRead =
        new(
            "notifications.mark_inbox_message_read",
            """
            UPDATE fn_notifications_inbox_message
            SET Status = @ReadStatus,
                ReadAtUtc = @ReadAtUtc
            WHERE Id = @Id
              AND RecipientUserId = @RecipientUserId
              AND TenantId IS NULL
              AND Status = @UnreadStatus
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkAllRead =
        new(
            "notifications.mark_all_inbox_messages_read",
            """
            UPDATE fn_notifications_inbox_message
            SET Status = @ReadStatus,
                ReadAtUtc = @ReadAtUtc
            WHERE RecipientUserId = @RecipientUserId
              AND TenantId IS NULL
              AND Status = @UnreadStatus
            """,
            SqlDataScope.HostOnly);
}
