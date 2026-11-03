using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Security;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features;

/// <summary>同一本地事务持有文档根锁，统一写入、保留、ACL 与状态的串行顺序。</summary>
internal sealed class DocumentMutationGuard(IQueryExecutor queries, IOptions<DatabaseOptions> database)
{
    internal async Task<DocumentItemDetailRecord?> LockAsync(Guid id, CancellationToken token)
    {
        // 锁定读取直接返回最新根快照；MySQL 普通 SELECT 可能仍看到本事务先前的一致性快照。
        return await queries.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(database.Value.Provider == DatabaseProvider.MySql
            ? DocumentItemSql.LockMySql : DocumentItemSql.LockSqlServer,
            DocumentSqlParameters.Create(("Id", id)), token).ConfigureAwait(false);
    }
}
