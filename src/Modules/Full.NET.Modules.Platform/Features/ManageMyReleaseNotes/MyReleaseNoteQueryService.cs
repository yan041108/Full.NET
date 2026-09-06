using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Platform.Features.ManageMyReleaseNotes;

/// <summary>当前用户已发布更新日志查询。</summary>
internal sealed class MyReleaseNoteQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>
    /// 分页查询当前用户可见的已发布更新日志，按版本排序键倒序排列。
    /// </summary>
    /// <param name="userId">当前用户标识。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<MyReleaseNoteResponse>>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var parameters = BuildUserParameters(userId, offset, pageSize);

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                ReleaseNoteSql.CountPublishedForUser,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<MyReleaseNoteRecord>(
                ResolveListStatement(),
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<MyReleaseNoteResponse>>.Success(
            new PagedResult<MyReleaseNoteResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>
    /// 查询当前用户最新一条未读已发布更新日志；不存在时返回空结果。
    /// </summary>
    /// <param name="userId">当前用户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最新未读更新日志或空。</returns>
    public async Task<Result<MyReleaseNoteResponse?>> GetLatestUnreadAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<MyReleaseNoteRecord>(
                ResolveLatestUnreadStatement(),
                BuildUserParameters(userId, offset: 0, pageSize: 1),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<MyReleaseNoteResponse?>.Success(record is null ? null : Map(record));
    }

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ReleaseNoteSql.ListPublishedForUserSqlServer,
            DatabaseProvider.MySql => ReleaseNoteSql.ListPublishedForUserMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };

    private SqlStatement ResolveLatestUnreadStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ReleaseNoteSql.FindLatestUnreadForUser,
            DatabaseProvider.MySql => ReleaseNoteSql.FindLatestUnreadForUserMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };

    internal static MyReleaseNoteResponse Map(MyReleaseNoteRecord record) =>
        new(
            record.Id,
            record.VersionLabel,
            record.VersionSortKey,
            record.Title,
            record.Content,
            record.PublishedAtUtc,
            record.IsRead,
            record.ReadAtUtc);

    private static Dictionary<string, object?> BuildUserParameters(
        Guid userId,
        int offset,
        int pageSize) =>
        ReleaseNoteSqlParameters.Create(
            ("UserId", userId),
            ("PublishedStatus", ReleaseNoteStatuses.Published),
            ("Offset", offset),
            ("PageSize", pageSize));
}
