using System.Text.Json;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Notifications.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ManageMyHostAnnouncements;

/// <summary>当前 Host 用户收到的公告列表、详情与未读计数。</summary>
internal sealed class MyHostAnnouncementQueryService(
    IQueryExecutor queryExecutor,
    HostAnnouncementRecipientGuard recipientGuard,
    IOptions<DatabaseOptions>? databaseOptions = null)
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
        var parameters = await BuildReadParametersAsync(userId, filter, cancellationToken).ConfigureAwait(false);
        parameters["Offset"] = checked((page - 1) * pageSize);
        parameters["PageSize"] = pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(CountStatement(), parameters, cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<ReceivedHostAnnouncementRecord>(ListStatement(), parameters, cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<ReceivedHostAnnouncementListItemResponse>>.Success(
            new(rows.Select(MapListItem).ToArray(), page, pageSize, total));
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
        var parameters = await BuildReadParametersAsync(userId, null, cancellationToken).ConfigureAwait(false);
        parameters["IsRead"] = 0;
        var count = await queryExecutor.QuerySingleOrDefaultAsync<int>(CountStatement(), parameters, cancellationToken)
            .ConfigureAwait(false);
        return Result<HostAnnouncementUnreadCountResponse>.Success(new(count));
    }

    private SqlStatement CountStatement() => databaseOptions?.Value.Provider == DatabaseProvider.MySql
        ? AnnouncementReadReceiptSql.CountReceivedMySql : AnnouncementReadReceiptSql.CountReceivedSqlServer;

    private SqlStatement ListStatement() => databaseOptions?.Value.Provider == DatabaseProvider.MySql
        ? AnnouncementReadReceiptSql.ListReceivedMySql : AnnouncementReadReceiptSql.ListReceivedSqlServer;

    private async Task<Dictionary<string, object?>> BuildReadParametersAsync(
        Guid userId, ReceivedHostAnnouncementListFilter? filter, CancellationToken cancellationToken)
    {
        var parameters = BuildUserParameters(userId);
        var memberships = await recipientGuard.ListMembershipsAsync(userId, cancellationToken).ConfigureAwait(false);
        parameters["Memberships"] = JsonSerializer.Serialize(memberships,
            NotificationsJsonSerializerContext.Default.IReadOnlyListTenantOrganizationMembershipEntry);
        parameters["Title"] = string.IsNullOrWhiteSpace(filter?.Title) ? null : $"%{filter.Title.Trim().ToLowerInvariant().Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![")}%";
        parameters["IsRead"] = filter?.IsRead is { } isRead ? (isRead ? 1 : 0) : null;
        return parameters;
    }

    private async Task<bool> IsVisibleAsync(
        Guid userId,
        ReceivedHostAnnouncementRecord record,
        CancellationToken cancellationToken)
    {
        if (record.AudienceKind is AnnouncementAudienceKinds.All or AnnouncementAudienceKinds.Users)
        {
            if (record.AudienceKind == AnnouncementAudienceKinds.All) return true;
            var directTargets = await LoadTargetsAsync([record.Id], cancellationToken).ConfigureAwait(false);
            return directTargets.GetUserIds(record.Id).Contains(userId);
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
