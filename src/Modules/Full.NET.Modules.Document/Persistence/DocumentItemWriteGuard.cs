using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>版本与回收站事务共用文档主行锁，主行先于子行；调用方仍须复核活动/删除状态。</summary>
internal sealed class DocumentItemWriteGuard(IQueryExecutor queries, IOptions<DatabaseOptions> options)
{
    public async Task<bool> LockAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var statement = options.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentItemSql.LockHostItemSqlServer,
            DatabaseProvider.MySql => DocumentItemSql.LockHostItemMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        return await queries.QuerySingleOrDefaultAsync<int>(statement,
            DocumentSqlParameters.Create(("Id", itemId)), cancellationToken).ConfigureAwait(false) == 1;
    }
}
