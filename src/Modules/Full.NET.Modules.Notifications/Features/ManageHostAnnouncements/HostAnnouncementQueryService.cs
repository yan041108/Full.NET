using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;

/// <summary>Host 公告分页查询。</summary>
internal sealed class HostAnnouncementQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>
    /// 分页查询 Host 公告，按创建时间倒序排列，并支持标题与状态过滤。
    /// </summary>
    public async Task<Result<PagedResult<HostAnnouncementResponse>>> ListAsync(
        int page,
        int pageSize,
        HostAnnouncementListFilter filter,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var parameters = BuildFilterParameters(filter, offset, pageSize);

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                AnnouncementSql.CountHost,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AnnouncementRecord>(
                ResolveListStatement(),
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rowArray = rows.ToArray();
        var targets = await LoadTargetsAsync(
                rowArray.Select(row => row.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<HostAnnouncementResponse>>.Success(
            new PagedResult<HostAnnouncementResponse>(
                rowArray.Select(row => Map(row, targets)).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>
    /// 按标识查询单条 Host 公告；不存在时返回未找到错误。
    /// </summary>
    public async Task<Result<HostAnnouncementResponse>> GetByIdAsync(
        Guid announcementId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<AnnouncementRecord>(
                AnnouncementSql.FindHostById,
                new Dictionary<string, object?> { ["Id"] = announcementId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var targets = await LoadTargetsAsync([announcementId], cancellationToken)
            .ConfigureAwait(false);
        return Result<HostAnnouncementResponse>.Success(Map(record, targets));
    }

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AnnouncementSql.ListHostSqlServer,
            DatabaseProvider.MySql => AnnouncementSql.ListHostMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };

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

    private static Dictionary<string, object?> BuildFilterParameters(
        HostAnnouncementListFilter filter,
        int offset,
        int pageSize)
    {
        var title = string.IsNullOrWhiteSpace(filter.Title) ? null : filter.Title.Trim();
        return new Dictionary<string, object?>
        {
            ["Offset"] = offset,
            ["PageSize"] = pageSize,
            ["Title"] = title,
            ["TitlePattern"] = title is null ? null : $"%{title}%",
            ["Status"] = NormalizeFilterValue(filter.Status),
            ["Kind"] = NormalizeFilterValue(filter.Kind),
            ["AudienceKind"] = NormalizeFilterValue(filter.AudienceKind),
        };
    }

    private static string? NormalizeFilterValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static HostAnnouncementResponse Map(
        AnnouncementRecord record,
        AnnouncementTargetBundle targets) =>
        new(
            record.Id,
            record.Title,
            record.Content,
            record.Kind,
            record.AudienceKind,
            record.Status,
            record.PublishedAtUtc,
            record.PublishedByUserId,
            record.RetractedAtUtc,
            record.RetractedByUserId,
            targets.GetUserIds(record.Id),
            targets.GetOrganizations(record.Id),
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<HostAnnouncementResponse> NotFound() =>
        Result<HostAnnouncementResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementNotFound,
            "The host announcement was not found.",
            ErrorType.NotFound));
}

/// <summary>公告受众子表批量查询结果。</summary>
internal sealed class AnnouncementTargetBundle
{
    public static AnnouncementTargetBundle Empty { get; } = new([], []);

    private readonly ILookup<Guid, Guid> _users;
    private readonly ILookup<Guid, HostAnnouncementTargetOrganization> _organizations;

    private AnnouncementTargetBundle(
        IEnumerable<AnnouncementTargetUserRecord> users,
        IEnumerable<AnnouncementTargetOrganizationRecord> organizations)
    {
        _users = users.ToLookup(row => row.AnnouncementId, row => row.UserId);
        _organizations = organizations.ToLookup(
            row => row.AnnouncementId,
            row => new HostAnnouncementTargetOrganization(row.TenantId, row.OrganizationUnitId));
    }

    public static AnnouncementTargetBundle From(
        IEnumerable<AnnouncementTargetUserRecord> users,
        IEnumerable<AnnouncementTargetOrganizationRecord> organizations) =>
        new(users, organizations);

    public IReadOnlyList<Guid> GetUserIds(Guid announcementId) =>
        _users[announcementId].OrderBy(id => id).ToArray();

    public IReadOnlyList<HostAnnouncementTargetOrganization> GetOrganizations(Guid announcementId) =>
        _organizations[announcementId]
            .OrderBy(target => target.TenantId)
            .ThenBy(target => target.OrganizationUnitId)
            .ToArray();
}
