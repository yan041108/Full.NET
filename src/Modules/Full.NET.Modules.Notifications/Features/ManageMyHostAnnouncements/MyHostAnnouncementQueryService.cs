using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ManageMyHostAnnouncements;

/// <summary>当前 Host 用户收到的公告列表、详情与未读计数。</summary>
internal sealed class MyHostAnnouncementQueryService(
    IQueryExecutor queryExecutor,
    HostAnnouncementRecipientGuard recipientGuard)
{
    /// <summary>分页列出当前用户可见的已发布公告。</summary>
    public async Task<Result<PagedResult<ReceivedHostAnnouncementListItemResponse>>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        ReceivedHostAnnouncementListFilter? filter,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var visible = await LoadVisibleAnnouncementsAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        var filtered = ApplyFilter(visible, filter)
            .OrderByDescending(item => item.PublishedAtUtc)
            .ThenBy(item => item.Id)
            .ToArray();
        var offset = (page - 1) * pageSize;
        var pageItems = filtered
            .Skip(offset)
            .Take(pageSize)
            .Select(MapListItem)
            .ToArray();
        return Result<PagedResult<ReceivedHostAnnouncementListItemResponse>>.Success(
            new PagedResult<ReceivedHostAnnouncementListItemResponse>(
                pageItems,
                page,
                pageSize,
                filtered.Length));
    }

    /// <summary>查询单条可见公告详情。</summary>
    public async Task<Result<ReceivedHostAnnouncementDetailResponse>> GetByIdAsync(
        Guid userId,
        Guid announcementId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<ReceivedHostAnnouncementRecord>(
                AnnouncementReadReceiptSql.FindReceivedById,
                BuildUserParameters(userId, announcementId),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (!await IsVisibleAsync(userId, record, cancellationToken).ConfigureAwait(false))
        {
            return NotRecipient();
        }

        return Result<ReceivedHostAnnouncementDetailResponse>.Success(MapDetail(record));
    }

    /// <summary>统计当前用户可见公告的未读数量。</summary>
    public async Task<Result<HostAnnouncementUnreadCountResponse>> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var visible = await LoadVisibleAnnouncementsAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        var unreadCount = visible.Count(item => item.ReadAtUtc is null);
        return Result<HostAnnouncementUnreadCountResponse>.Success(
            new HostAnnouncementUnreadCountResponse(unreadCount));
    }

    private async Task<IReadOnlyList<ReceivedHostAnnouncementRecord>> LoadVisibleAnnouncementsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var parameters = BuildUserParameters(userId);
        var directRows = await queryExecutor.QueryAsync<ReceivedHostAnnouncementRecord>(
                AnnouncementReadReceiptSql.ListDirectAudienceCandidates,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var organizationRows = await queryExecutor.QueryAsync<ReceivedHostAnnouncementRecord>(
                AnnouncementReadReceiptSql.ListOrganizationAudienceCandidates,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (organizationRows.Count == 0)
        {
            return directRows.ToArray();
        }

        var targets = await LoadTargetsAsync(
                organizationRows.Select(row => row.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var visibleOrganizationIds = await recipientGuard.FilterVisibleOrganizationAnnouncementsAsync(
                userId,
                organizationRows.ToArray(),
                targets,
                cancellationToken)
            .ConfigureAwait(false);
        return directRows
            .Concat(organizationRows.Where(row => visibleOrganizationIds.Contains(row.Id)))
            .GroupBy(row => row.Id)
            .Select(group => group.First())
            .ToArray();
    }

    private async Task<bool> IsVisibleAsync(
        Guid userId,
        ReceivedHostAnnouncementRecord record,
        CancellationToken cancellationToken)
    {
        if (record.AudienceKind is AnnouncementAudienceKinds.All or AnnouncementAudienceKinds.Users)
        {
            var parameters = BuildUserParameters(userId);
            var directRows = await queryExecutor.QueryAsync<ReceivedHostAnnouncementRecord>(
                    AnnouncementReadReceiptSql.ListDirectAudienceCandidates,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            return directRows.Any(row => row.Id == record.Id);
        }

        var targets = await LoadTargetsAsync([record.Id], cancellationToken)
            .ConfigureAwait(false);
        var announcement = new AnnouncementRecord
        {
            Id = record.Id,
            AudienceKind = record.AudienceKind,
            Status = AnnouncementStatuses.Published,
        };
        return await recipientGuard.IsRecipientAsync(
                userId,
                announcement,
                targets,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AnnouncementTargetBundle> LoadTargetsAsync(
        IReadOnlyCollection<Guid> announcementIds,
        CancellationToken cancellationToken)
    {
        if (announcementIds.Count == 0)
        {
            return AnnouncementTargetBundle.Empty;
        }

        var users = await queryExecutor.QueryAsync<AnnouncementTargetUserRecord>(
                AnnouncementTargetSql.ListUsersByAnnouncementIds,
                new Dictionary<string, object?> { ["AnnouncementIds"] = announcementIds },
                cancellationToken)
            .ConfigureAwait(false);
        var organizations = await queryExecutor.QueryAsync<AnnouncementTargetOrganizationRecord>(
                AnnouncementTargetSql.ListOrganizationsByAnnouncementIds,
                new Dictionary<string, object?> { ["AnnouncementIds"] = announcementIds },
                cancellationToken)
            .ConfigureAwait(false);
        return AnnouncementTargetBundle.From(users, organizations);
    }

    private static IEnumerable<ReceivedHostAnnouncementRecord> ApplyFilter(
        IReadOnlyList<ReceivedHostAnnouncementRecord> rows,
        ReceivedHostAnnouncementListFilter? filter)
    {
        var title = string.IsNullOrWhiteSpace(filter?.Title) ? null : filter!.Title.Trim();
        return rows.Where(row =>
        {
            if (title is not null
                && !row.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (filter?.IsRead is bool isRead)
            {
                var rowIsRead = row.ReadAtUtc is not null;
                if (rowIsRead != isRead)
                {
                    return false;
                }
            }

            return true;
        });
    }

    private static Dictionary<string, object?> BuildUserParameters(
        Guid userId,
        Guid? announcementId = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["UserId"] = userId,
            ["PublishedStatus"] = AnnouncementStatuses.Published,
            ["AudienceAll"] = AnnouncementAudienceKinds.All,
            ["AudienceUsers"] = AnnouncementAudienceKinds.Users,
            ["AudienceOrganizations"] = AnnouncementAudienceKinds.Organizations,
        };
        if (announcementId is not null)
        {
            parameters["AnnouncementId"] = announcementId.Value;
        }

        return parameters;
    }

    private static ReceivedHostAnnouncementListItemResponse MapListItem(
        ReceivedHostAnnouncementRecord record) =>
        new(
            record.Id,
            record.Title,
            record.Kind,
            record.AudienceKind,
            record.PublishedAtUtc,
            record.ReadAtUtc is not null,
            record.ReadAtUtc);

    private static ReceivedHostAnnouncementDetailResponse MapDetail(
        ReceivedHostAnnouncementRecord record) =>
        new(
            record.Id,
            record.Title,
            record.Content,
            record.Kind,
            record.AudienceKind,
            record.PublishedAtUtc,
            record.PublishedByUserId,
            record.ReadAtUtc is not null,
            record.ReadAtUtc);

    private static Result<ReceivedHostAnnouncementDetailResponse> NotFound() =>
        Result<ReceivedHostAnnouncementDetailResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementNotFound,
            "The host announcement was not found.",
            ErrorType.NotFound));

    private static Result<ReceivedHostAnnouncementDetailResponse> NotRecipient() =>
        Result<ReceivedHostAnnouncementDetailResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementNotRecipient,
            "The current user is not in the announcement audience.",
            ErrorType.Forbidden));
}
