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
