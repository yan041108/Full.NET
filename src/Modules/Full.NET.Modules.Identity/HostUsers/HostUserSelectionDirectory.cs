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
    IOptions<DatabaseOptions> databaseOptions) : IHostUserSelectionDirectory
{
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
}
