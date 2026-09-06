using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;

/// <summary>发布方查看 Host 公告阅读统计与已读明细。</summary>
internal sealed class HostAnnouncementReadStatsQueryService(
    IQueryExecutor queryExecutor,
    HostAnnouncementQueryService announcementQueries,
    IHostActiveUserCountReader hostActiveUserCountReader,
    ITenantOrganizationUserMembershipReader membershipReader,
    IHostUserDisplayDirectory hostUserDisplayDirectory,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>读取公告阅读统计摘要。</summary>
    public async Task<Result<HostAnnouncementReadStatsResponse>> GetStatsAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default)
    {
        var announcement = await announcementQueries.GetByIdAsync(announcementId, cancellationToken)
            .ConfigureAwait(false);
        if (!announcement.IsSuccess)
        {
            return Result<HostAnnouncementReadStatsResponse>.Failure(announcement.Error!);
        }

        if (!IsStatsEligibleStatus(announcement.Value!.Status))
        {
            return Result<HostAnnouncementReadStatsResponse>.Failure(new Error(
                NotificationsErrorCodes.AnnouncementInvalidStatus,
                "Read statistics are only available for published or retracted announcements.",
                ErrorType.Validation));
        }

        var eligibleRecipientCount = await ResolveEligibleRecipientCountAsync(
                announcement.Value,
                cancellationToken)
            .ConfigureAwait(false);
        var readCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                AnnouncementReadReceiptSql.CountReadsByAnnouncement,
                new Dictionary<string, object?> { ["AnnouncementId"] = announcementId },
                cancellationToken)
            .ConfigureAwait(false);
        var unreadCount = Math.Max(0, eligibleRecipientCount - readCount);
        return Result<HostAnnouncementReadStatsResponse>.Success(
            new HostAnnouncementReadStatsResponse(
                eligibleRecipientCount,
                readCount,
                unreadCount));
    }

    /// <summary>分页列出公告已读回执。</summary>
    public async Task<Result<PagedResult<HostAnnouncementReadReceiptResponse>>> ListReceiptsAsync(
        Guid announcementId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var announcement = await announcementQueries.GetByIdAsync(announcementId, cancellationToken)
            .ConfigureAwait(false);
        if (!announcement.IsSuccess)
        {
            return Result<PagedResult<HostAnnouncementReadReceiptResponse>>.Failure(announcement.Error!);
        }

        if (!IsStatsEligibleStatus(announcement.Value!.Status))
        {
            return Result<PagedResult<HostAnnouncementReadReceiptResponse>>.Failure(new Error(
                NotificationsErrorCodes.AnnouncementInvalidStatus,
                "Read receipts are only available for published or retracted announcements.",
                ErrorType.Validation));
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                AnnouncementReadReceiptSql.CountReadsByAnnouncement,
                new Dictionary<string, object?> { ["AnnouncementId"] = announcementId },
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AnnouncementReadReceiptRecord>(
                ResolveReceiptListStatement(),
                new Dictionary<string, object?>
                {
                    ["AnnouncementId"] = announcementId,
                    ["Offset"] = offset,
                    ["PageSize"] = pageSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var userIds = rows.Select(row => row.UserId).ToArray();
        var users = userIds.Length == 0
            ? new Dictionary<Guid, HostUserDirectoryEntry>()
            : await hostUserDisplayDirectory.FindHostUsersAsync(userIds, cancellationToken)
                .ConfigureAwait(false);
        var items = rows
            .Select(row =>
            {
                users.TryGetValue(row.UserId, out var user);
                return new HostAnnouncementReadReceiptResponse(
                    row.UserId,
                    user?.Username,
                    user?.DisplayName,
                    row.ReadAtUtc);
            })
            .ToArray();
        return Result<PagedResult<HostAnnouncementReadReceiptResponse>>.Success(
            new PagedResult<HostAnnouncementReadReceiptResponse>(
                items,
                page,
                pageSize,
                total));
    }

    private async Task<long> ResolveEligibleRecipientCountAsync(
        HostAnnouncementResponse announcement,
        CancellationToken cancellationToken)
    {
        return announcement.AudienceKind switch
        {
            AnnouncementAudienceKinds.All => await hostActiveUserCountReader.CountActiveHostUsersAsync(
                    cancellationToken)
                .ConfigureAwait(false),
            AnnouncementAudienceKinds.Users => await queryExecutor.QuerySingleOrDefaultAsync<long>(
                    AnnouncementReadReceiptSql.CountTargetUsersByAnnouncement,
                    new Dictionary<string, object?> { ["AnnouncementId"] = announcement.Id },
                    cancellationToken)
                .ConfigureAwait(false),
            AnnouncementAudienceKinds.Organizations => await membershipReader.CountDistinctActiveMembersAsync(
                    announcement.TargetOrganizations
                        .Select(target => new TenantOrganizationMembershipTarget(
                            target.TenantId,
                            target.OrganizationUnitId))
                        .ToArray(),
                    cancellationToken)
                .ConfigureAwait(false),
            _ => 0,
        };
    }

    private static bool IsStatsEligibleStatus(string status) =>
        string.Equals(status, AnnouncementStatuses.Published, StringComparison.Ordinal)
        || string.Equals(status, AnnouncementStatuses.Retracted, StringComparison.Ordinal);

    private SqlStatement ResolveReceiptListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AnnouncementReadReceiptSql.ListReceiptsByAnnouncementSqlServer,
            DatabaseProvider.MySql => AnnouncementReadReceiptSql.ListReceiptsByAnnouncementMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
}
