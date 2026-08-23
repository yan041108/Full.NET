using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>
/// 站内信表的参数化 SQL 语句集合，全部声明为 <see cref="SqlDataScope.HostOnly"/>。
/// </summary>
/// <remarks>
/// 站内信属 Host 作用域，行守卫以 <c>TenantId IS NULL</c> 表达；
/// 列表查询与未读计数分别提供 SQL Server 的 <c>OFFSET/FETCH</c> 与 MySQL 的 <c>LIMIT/OFFSET</c> 成对实现。
/// </remarks>
internal static class InboxMessageSql
{
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
