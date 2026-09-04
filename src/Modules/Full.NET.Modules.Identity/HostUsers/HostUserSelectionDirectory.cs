using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.HostUsers;

/// <summary>
/// 提供跨模块可分配 Host 用户分页投影，并保持数据库提供程序分页语义一致。
/// </summary>
internal sealed class HostUserSelectionDirectory(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions) : IHostUserSelectionDirectory, IHostUserBatchSelectionDirectory
{
    /// <summary>分页读取活动 Host 用户的最小候选投影。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动 Host 用户分页结果。</returns>
    public async Task<PagedResult<HostUserDirectoryEntry>> ListActiveHostUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveHostUserSelections,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                IdentitySql.ListActiveHostUserSelectionsSqlServer,
            DatabaseProvider.MySql =>
                IdentitySql.ListActiveHostUserSelectionsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var records = await queryExecutor.QueryAsync<HostUserDirectoryRecord>(
                statement,
                IdentitySqlParameters.Create(("Offset", offset), ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = records
            .Select(record => new HostUserDirectoryEntry(
                record.Id,
                record.Username,
                record.DisplayName))
            .ToArray();

        return new PagedResult<HostUserDirectoryEntry>(
            items,
            page,
            pageSize,
            total);
    }

    /// <summary>批量查找仍处于活动状态的指定 Host 用户。</summary>
    /// <param name="userIds">待校验的稳定 Host 用户标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动 Host 用户的去重字典。</returns>
    public async Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindActiveHostUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var distinctUserIds = userIds.Distinct().ToArray();
        if (distinctUserIds.Length == 0)
        {
            return new Dictionary<Guid, HostUserDirectoryEntry>();
        }

        var records = await queryExecutor.QueryAsync<HostUserDirectoryRecord>(
                IdentitySql.ListActiveHostUserSelectionsByIds,
                IdentitySqlParameters.Create(("UserIds", distinctUserIds)),
                cancellationToken)
            .ConfigureAwait(false);
        return records.ToDictionary(
            record => record.Id,
            record => new HostUserDirectoryEntry(
                record.Id,
                record.Username,
                record.DisplayName));
    }
}
