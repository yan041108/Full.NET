using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>
/// 站内信表的参数化 SQL。Host 写入保持 HostOnly；租户写入走 TenantRequired；
/// 当前用户读写使用 Global 并显式携带受信 TenantScopeKey，供 Worker 在 Host 上下文重建未读数。
/// TenantRequired 的 INSERT...SELECT 必须在 WHERE 中比较 <c>TenantId = @TenantId</c>，
/// 守卫不把 SELECT 列表中的 <c>@TenantId</c> 视为安全子句。
/// </summary>
internal static class InboxMessageSql
{
    public static readonly SqlStatement InsertHost =
        new(
            "notifications.insert_inbox_message.host",
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status,
                 ReadAtUtc, CreatedAtUtc, CreatedByUserId, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@Id, NULL, @RecipientUserId, @Title, @Content, @Status,
                 NULL, @CreatedAtUtc, @CreatedByUserId, 'host', 'host', NULL)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertTenant =
        new(
            "notifications.insert_inbox_message.tenant",
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status,
                 ReadAtUtc, CreatedAtUtc, CreatedByUserId, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@Id, @TenantId, @RecipientUserId, @Title, @Content, @Status,
                 NULL, @CreatedAtUtc, @CreatedByUserId, 'tenant', @TenantScopeKey, NULL)
            """,
            SqlDataScope.TenantRequired,
            SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertHostForIntent =
        new(
            "notifications.insert_inbox_message.host_intent",
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status,
                 ReadAtUtc, CreatedAtUtc, CreatedByUserId, ScopeKey, TenantScopeKey, IntentId)
            SELECT @Id, NULL, @RecipientUserId, @Title, @Content, @Status,
                   NULL, @CreatedAtUtc, @CreatedByUserId, 'host', 'host', @IntentId
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_notifications_inbox_message
                WHERE TenantScopeKey = 'host'
                  AND IntentId = @IntentId
                  AND RecipientUserId = @RecipientUserId)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertTenantForIntent =
        new(
            "notifications.insert_inbox_message.tenant_intent",
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status,
                 ReadAtUtc, CreatedAtUtc, CreatedByUserId, ScopeKey, TenantScopeKey, IntentId)
            SELECT @Id, @TenantId, @RecipientUserId, @Title, @Content, @Status,
                   NULL, @CreatedAtUtc, @CreatedByUserId, 'tenant', @TenantScopeKey, @IntentId
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_notifications_inbox_message
                WHERE TenantScopeKey = @TenantScopeKey
                  AND TenantId = @TenantId
                  AND IntentId = @IntentId
                  AND RecipientUserId = @RecipientUserId)
            """,
            SqlDataScope.TenantRequired,
            SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListForRecipientSqlServer =
        new(
            "notifications.list_inbox_messages.sql_server",
            $"""
            SELECT Id, TenantId, RecipientUserId, Title, Content, Status,
                   ReadAtUtc, CreatedAtUtc, CreatedByUserId, ScopeKey, TenantScopeKey, IntentId
            FROM fn_notifications_inbox_message
            WHERE {RecipientWhereClause}
            ORDER BY CreatedAtUtc DESC, Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement ListForRecipientMySql =
        new(
            "notifications.list_inbox_messages.mysql",
            $"""
            SELECT Id, TenantId, RecipientUserId, Title, Content, Status,
                   ReadAtUtc, CreatedAtUtc, CreatedByUserId, ScopeKey, TenantScopeKey, IntentId
            FROM fn_notifications_inbox_message
            WHERE {RecipientWhereClause}
            ORDER BY CreatedAtUtc DESC, Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement CountForRecipient =
        new(
            "notifications.count_inbox_messages",
            $"""
            SELECT COUNT(*)
            FROM fn_notifications_inbox_message
            WHERE {RecipientWhereClause}
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement CountUnreadForRecipient =
        new(
            "notifications.count_unread_inbox_messages",
            """
            SELECT COUNT(*)
            FROM fn_notifications_inbox_message
            WHERE RecipientUserId = @RecipientUserId
              AND TenantScopeKey = @TenantScopeKey
              AND Status = @UnreadStatus
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindForRecipientById =
        new(
            "notifications.find_inbox_message_for_recipient",
            """
            SELECT Id, TenantId, RecipientUserId, Title, Content, Status,
                   ReadAtUtc, CreatedAtUtc, CreatedByUserId, ScopeKey, TenantScopeKey, IntentId
            FROM fn_notifications_inbox_message
            WHERE Id = @Id
              AND RecipientUserId = @RecipientUserId
              AND TenantScopeKey = @TenantScopeKey
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement FindByIntentRecipient =
        new(
            "notifications.find_inbox_message_by_intent_recipient",
            """
            SELECT Id, TenantId, RecipientUserId, Title, Content, Status,
                   ReadAtUtc, CreatedAtUtc, CreatedByUserId, ScopeKey, TenantScopeKey, IntentId
            FROM fn_notifications_inbox_message
            WHERE IntentId = @IntentId
              AND RecipientUserId = @RecipientUserId
              AND TenantScopeKey = @TenantScopeKey
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement MarkRead =
        new(
            "notifications.mark_inbox_message_read",
            """
            UPDATE fn_notifications_inbox_message
            SET Status = @ReadStatus,
                ReadAtUtc = @ReadAtUtc
            WHERE Id = @Id
              AND RecipientUserId = @RecipientUserId
              AND TenantScopeKey = @TenantScopeKey
              AND Status = @UnreadStatus
            """,
            SqlDataScope.Global);

    public static readonly SqlStatement MarkAllRead =
        new(
            "notifications.mark_all_inbox_messages_read",
            """
            UPDATE fn_notifications_inbox_message
            SET Status = @ReadStatus,
                ReadAtUtc = @ReadAtUtc
            WHERE RecipientUserId = @RecipientUserId
              AND TenantScopeKey = @TenantScopeKey
              AND Status = @UnreadStatus
            """,
            SqlDataScope.Global);

    private const string RecipientWhereClause =
        """
        RecipientUserId = @RecipientUserId
          AND TenantScopeKey = @TenantScopeKey
          AND (@Title IS NULL OR Title LIKE @TitlePattern)
          AND (@Status IS NULL OR Status = @Status)
        """;
}
