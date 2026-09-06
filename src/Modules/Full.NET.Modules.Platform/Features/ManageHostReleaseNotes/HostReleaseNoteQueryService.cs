using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Platform.Features.ManageHostReleaseNotes;

/// <summary>Host 更新日志分页查询。</summary>
internal sealed class HostReleaseNoteQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>
    /// 分页查询 Host 更新日志，按版本排序键倒序排列。
    /// </summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="filter">过滤条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果或稳定业务错误。</returns>
    public async Task<Result<PagedResult<HostReleaseNoteResponse>>> ListAsync(
        int page,
        int pageSize,
        HostReleaseNoteListFilter filter,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var parameters = BuildFilterParameters(filter, offset, pageSize);

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                ReleaseNoteSql.CountHost,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<ReleaseNoteRecord>(
                ResolveListStatement(),
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<HostReleaseNoteResponse>>.Success(
            new PagedResult<HostReleaseNoteResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>
    /// 按标识查询单条 Host 更新日志；不存在时返回未找到错误。
    /// </summary>
    /// <param name="releaseNoteId">更新日志标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新日志详情或稳定业务错误。</returns>
    public async Task<Result<HostReleaseNoteResponse>> GetByIdAsync(
        Guid releaseNoteId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<ReleaseNoteRecord>(
                ReleaseNoteSql.FindHostById,
                ReleaseNoteSqlParameters.Create(("Id", releaseNoteId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<HostReleaseNoteResponse>.Success(Map(record));
    }

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ReleaseNoteSql.ListHostSqlServer,
            DatabaseProvider.MySql => ReleaseNoteSql.ListHostMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };

    internal static HostReleaseNoteResponse Map(ReleaseNoteRecord record) =>
        new(
            record.Id,
            record.VersionLabel,
            record.VersionSortKey,
            record.Title,
            record.Content,
            record.Status,
            record.PublishedAtUtc,
            record.PublishedByUserId,
            record.RetractedAtUtc,
            record.RetractedByUserId,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Dictionary<string, object?> BuildFilterParameters(
        HostReleaseNoteListFilter filter,
        int offset,
        int pageSize)
    {
        var title = string.IsNullOrWhiteSpace(filter.Title) ? null : filter.Title.Trim();
        var versionLabel = string.IsNullOrWhiteSpace(filter.VersionLabel)
            ? null
            : filter.VersionLabel.Trim();
        return ReleaseNoteSqlParameters.Create(
            ("Offset", offset),
            ("PageSize", pageSize),
            ("Title", title),
            ("TitlePattern", title is null ? null : $"%{title}%"),
            ("Status", NormalizeFilterValue(filter.Status)),
            ("VersionLabel", versionLabel),
            ("VersionLabelPattern", versionLabel is null ? null : $"%{versionLabel}%"));
    }

    private static string? NormalizeFilterValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<HostReleaseNoteResponse> NotFound() =>
        Result<HostReleaseNoteResponse>.Failure(new Error(
            PlatformErrorCodes.ReleaseNoteNotFound,
            "The release note was not found.",
            ErrorType.NotFound));
}
