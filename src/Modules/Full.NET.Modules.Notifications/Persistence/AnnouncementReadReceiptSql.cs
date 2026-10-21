using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>Host 公告已读回执与收件查询 SQL。</summary>
internal static class AnnouncementReadReceiptSql
{
    private const string ReceivedSelectColumns =
        """
        announcement.Id,
        announcement.Title,
        announcement.Content,
        announcement.Kind,
        announcement.AudienceKind,
        announcement.PublishedAtUtc,
        announcement.PublishedByUserId,
        receipt.ReadAtUtc
        """;

    private const string PublishedHostWhereClause =
        """
        announcement.TenantId IS NULL
          AND announcement.Status = @PublishedStatus
        """;

    public static readonly SqlStatement ListDirectAudienceCandidates =
        new(
            "notifications.announcement_received.list_direct_candidates",
            $"""
            SELECT {ReceivedSelectColumns}
            FROM fn_notifications_announcement AS announcement
            LEFT JOIN fn_notifications_announcement_read_receipt AS receipt
                ON receipt.AnnouncementId = announcement.Id
               AND receipt.UserId = @UserId
            WHERE {PublishedHostWhereClause}
              AND (
                    announcement.AudienceKind = @AudienceAll
                 OR (
                        announcement.AudienceKind = @AudienceUsers
                    AND EXISTS (
                        SELECT 1
                        FROM fn_notifications_announcement_target_user AS targetUser
                        WHERE targetUser.AnnouncementId = announcement.Id
                          AND targetUser.UserId = @UserId))
              )
            ORDER BY announcement.PublishedAtUtc DESC, announcement.Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListOrganizationAudienceCandidates =
        new(
            "notifications.announcement_received.list_organization_candidates",
            $"""
            SELECT {ReceivedSelectColumns}
            FROM fn_notifications_announcement AS announcement
            LEFT JOIN fn_notifications_announcement_read_receipt AS receipt
                ON receipt.AnnouncementId = announcement.Id
               AND receipt.UserId = @UserId
            WHERE {PublishedHostWhereClause}
              AND announcement.AudienceKind = @AudienceOrganizations
            ORDER BY announcement.PublishedAtUtc DESC, announcement.Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindReceivedById =
        new(
            "notifications.announcement_received.find_by_id",
            $"""
            SELECT {ReceivedSelectColumns}
            FROM fn_notifications_announcement AS announcement
            LEFT JOIN fn_notifications_announcement_read_receipt AS receipt
                ON receipt.AnnouncementId = announcement.Id
               AND receipt.UserId = @UserId
            WHERE announcement.Id = @AnnouncementId
              AND {PublishedHostWhereClause}
            """,
            SqlDataScope.HostOnly);

    private const string ReceivedFilter = """
        AND (@Title IS NULL OR LOWER(announcement.Title) LIKE @Title ESCAPE '!')
        AND (@IsRead IS NULL OR (@IsRead = 1 AND receipt.ReadAtUtc IS NOT NULL)
             OR (@IsRead = 0 AND receipt.ReadAtUtc IS NULL))
        AND (announcement.AudienceKind = @AudienceAll
             OR (announcement.AudienceKind = @AudienceUsers AND EXISTS (
                 SELECT 1 FROM fn_notifications_announcement_target_user targetUser
                 WHERE targetUser.AnnouncementId = announcement.Id AND targetUser.UserId = @UserId))
             OR (announcement.AudienceKind = @AudienceOrganizations AND EXISTS (
                 SELECT 1 FROM fn_notifications_announcement_target_organization targetOrganization
                 WHERE targetOrganization.AnnouncementId = announcement.Id
        """;

    // 成员关系由 Organization 权威端口解析，以单个 JSON 参数传入，避免集合参数上限与跨模块 JOIN。
    private const string MembershipSqlServer = """
                 AND EXISTS (SELECT 1 FROM OPENJSON(@Memberships) WITH (
                     TenantId uniqueidentifier '$.tenantId', OrganizationUnitId uniqueidentifier '$.organizationUnitId') membership
                     WHERE membership.TenantId = targetOrganization.TenantId
                       AND membership.OrganizationUnitId = targetOrganization.OrganizationUnitId))))
        """;
    private const string MembershipMySql = """
                 AND EXISTS (SELECT 1 FROM JSON_TABLE(@Memberships, '$[*]' COLUMNS (
                     TenantId VARCHAR(36) PATH '$.tenantId', OrganizationUnitId VARCHAR(36) PATH '$.organizationUnitId')) membership
                     WHERE UNHEX(REPLACE(membership.TenantId, '-', '')) = targetOrganization.TenantId
                       AND UNHEX(REPLACE(membership.OrganizationUnitId, '-', '')) = targetOrganization.OrganizationUnitId))))
        """;

    public static readonly SqlStatement ListReceivedSqlServer = new(
        "notifications.announcement_received.list.sqlserver",
        $"""
        SELECT announcement.Id, announcement.Title, '' AS Content, announcement.Kind, announcement.AudienceKind, announcement.PublishedAtUtc, announcement.PublishedByUserId, receipt.ReadAtUtc
        FROM fn_notifications_announcement announcement
        LEFT JOIN fn_notifications_announcement_read_receipt receipt
          ON receipt.AnnouncementId = announcement.Id AND receipt.UserId = @UserId
        WHERE {PublishedHostWhereClause}
        {ReceivedFilter}
        {MembershipSqlServer}
        ORDER BY announcement.PublishedAtUtc DESC, announcement.Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement CountReceivedSqlServer = new(
        "notifications.announcement_received.count.sqlserver",
        $"""
        SELECT COUNT(1)
        FROM fn_notifications_announcement announcement
        LEFT JOIN fn_notifications_announcement_read_receipt receipt
          ON receipt.AnnouncementId = announcement.Id AND receipt.UserId = @UserId
        WHERE {PublishedHostWhereClause}
        {ReceivedFilter}
        {MembershipSqlServer}

        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement ListReceivedMySql = new(
        "notifications.announcement_received.list.mysql",
        $"""
        SELECT announcement.Id, announcement.Title, '' AS Content, announcement.Kind, announcement.AudienceKind, announcement.PublishedAtUtc, announcement.PublishedByUserId, receipt.ReadAtUtc
        FROM fn_notifications_announcement announcement
        LEFT JOIN fn_notifications_announcement_read_receipt receipt
          ON receipt.AnnouncementId = announcement.Id AND receipt.UserId = @UserId
        WHERE {PublishedHostWhereClause}
        {ReceivedFilter}
        {MembershipMySql}
        ORDER BY announcement.PublishedAtUtc DESC, announcement.Id LIMIT @PageSize OFFSET @Offset
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement CountReceivedMySql = new(
        "notifications.announcement_received.count.mysql",
        $"""
        SELECT COUNT(1)
        FROM fn_notifications_announcement announcement
        LEFT JOIN fn_notifications_announcement_read_receipt receipt
          ON receipt.AnnouncementId = announcement.Id AND receipt.UserId = @UserId
        WHERE {PublishedHostWhereClause}
        {ReceivedFilter}
        {MembershipMySql}

        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertIfAbsent =
        new(
            "notifications.announcement_read_receipt.insert_if_absent",
            """
            INSERT INTO fn_notifications_announcement_read_receipt
                (Id, AnnouncementId, UserId, ReadAtUtc)
            SELECT @Id, @AnnouncementId, @UserId, @ReadAtUtc
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_notifications_announcement_read_receipt AS existing
                WHERE existing.AnnouncementId = @AnnouncementId
                  AND existing.UserId = @UserId)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountReadsByAnnouncement =
        new(
            "notifications.announcement_read_receipt.count_by_announcement",
            """
            SELECT COUNT(1)
            FROM fn_notifications_announcement_read_receipt
            WHERE AnnouncementId = @AnnouncementId
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListReceiptsByAnnouncementSqlServer =
        new(
            "notifications.announcement_read_receipt.list_by_announcement.sqlserver",
            """
            SELECT receipt.UserId, receipt.ReadAtUtc
            FROM fn_notifications_announcement_read_receipt AS receipt
            WHERE receipt.AnnouncementId = @AnnouncementId
            ORDER BY receipt.ReadAtUtc DESC, receipt.UserId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListReceiptsByAnnouncementMySql =
        new(
            "notifications.announcement_read_receipt.list_by_announcement.mysql",
            """
            SELECT receipt.UserId, receipt.ReadAtUtc
            FROM fn_notifications_announcement_read_receipt AS receipt
            WHERE receipt.AnnouncementId = @AnnouncementId
            ORDER BY receipt.ReadAtUtc DESC, receipt.UserId
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountTargetUsersByAnnouncement =
        new(
            "notifications.announcement_target_user.count_by_announcement",
            """
            SELECT COUNT(1)
            FROM fn_notifications_announcement_target_user
            WHERE AnnouncementId = @AnnouncementId
            """,
            SqlDataScope.HostOnly);
}

/// <summary>公告收件候选行投影。</summary>
internal sealed class ReceivedHostAnnouncementRecord
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string AudienceKind { get; init; } = string.Empty;

    public DateTimeOffset PublishedAtUtc { get; init; }

    public Guid? PublishedByUserId { get; init; }

    public DateTimeOffset? ReadAtUtc { get; init; }
}

/// <summary>公告已读回执行投影。</summary>
internal sealed class AnnouncementReadReceiptRecord
{
    public Guid UserId { get; init; }

    public DateTimeOffset ReadAtUtc { get; init; }
}
