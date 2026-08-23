using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>
/// Host 公告表的参数化 SQL 语句集合，全部声明为 <see cref="SqlDataScope.HostOnly"/>。
/// </summary>
/// <remarks>
/// 公告属 Host 作用域，行守卫以 <c>TenantId IS NULL</c> 表达；
/// 列表查询分别提供 SQL Server 的 <c>OFFSET/FETCH</c> 与 MySQL 的 <c>LIMIT/OFFSET</c> 成对实现。
/// <see cref="UpdateDraft"/> 与 <see cref="Publish"/> 以 <c>Status AND Version</c> 作为 CAS 并发守卫，
/// 影响行数为 0 即并发冲突，调用方据此返回冲突而非静默覆盖。
/// </remarks>
internal static class AnnouncementSql
{
    public static readonly SqlStatement ListHostSqlServer =
        new(
            "notifications.list_host_announcements.sql_server",
            """
            SELECT Id, TenantId, Title, Content, Status, PublishedAtUtc,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_notifications_announcement
            WHERE TenantId IS NULL
            ORDER BY CreatedAtUtc DESC, Id
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostMySql =
        new(
            "notifications.list_host_announcements.mysql",
            """
            SELECT Id, TenantId, Title, Content, Status, PublishedAtUtc,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_notifications_announcement
            WHERE TenantId IS NULL
            ORDER BY CreatedAtUtc DESC, Id
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountHost =
        new(
            "notifications.count_host_announcements",
            """
            SELECT COUNT(*)
            FROM fn_notifications_announcement
            WHERE TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostById =
        new(
            "notifications.find_host_announcement_by_id",
            """
            SELECT Id, TenantId, Title, Content, Status, PublishedAtUtc,
                   CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version
            FROM fn_notifications_announcement
            WHERE Id = @Id AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert =
        new(
            "notifications.insert_host_announcement",
            """
            INSERT INTO fn_notifications_announcement
                (Id, TenantId, Title, Content, Status, PublishedAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
            VALUES
                (@Id, NULL, @Title, @Content, @Status, NULL,
                 @CreatedAtUtc, NULL, @CreatedByUserId, NULL, @Version)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateDraft =
        new(
            "notifications.update_host_announcement_draft",
            """
            UPDATE fn_notifications_announcement
            SET Title = @Title,
                Content = @Content,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND TenantId IS NULL
              AND Status = @DraftStatus
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement Publish =
        new(
            "notifications.publish_host_announcement",
            """
            UPDATE fn_notifications_announcement
            SET Status = @PublishedStatus,
                PublishedAtUtc = @PublishedAtUtc,
                UpdatedAtUtc = @UpdatedAtUtc,
                UpdatedByUserId = @UpdatedByUserId,
                Version = @NextVersion
            WHERE Id = @Id
              AND TenantId IS NULL
              AND Status = @DraftStatus
              AND Version = @Version
            """,
            SqlDataScope.HostOnly);
}
