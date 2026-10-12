using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

/// <summary>在调用方事务内保护标题准入；有效分类由分类行锁保护，未分类使用独立门禁行。</summary>
internal sealed class DocumentItemTitleService(IQueryExecutor queries, IOptions<DatabaseOptions> options)
{
    // 与迁移 250 的生产安全技术行一致；API 只锁定它，不能运行时创建或修改种子。
    internal static readonly Guid UnclassifiedGuardId = Guid.Parse("01a12eb3-0272-733d-910a-117839ae8c66");

    public async Task<bool> HasConflictAsync(string title, Guid? categoryId, Guid? excludedId, CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            var statement = options.Value.Provider switch
            {
                DatabaseProvider.SqlServer => DocumentItemSql.LockUnclassifiedWriteSqlServer,
                DatabaseProvider.MySql => DocumentItemSql.LockUnclassifiedWriteMySql,
                _ => throw new InvalidOperationException("Document title locking requires SQL Server or MySQL."),
            };
            var acquired = await queries.QuerySingleOrDefaultAsync<int>(statement,
                DocumentSqlParameters.Create(("Id", UnclassifiedGuardId)), cancellationToken).ConfigureAwait(false);
            if (acquired != 1)
                throw new InvalidOperationException("Document write guard is missing; run the database migrator before accepting writes.");
        }

        // 历史数据可能已有多条同名记录；计数判冲突，不能假设查询最多返回一行。
        return await queries.QuerySingleOrDefaultAsync<long>(DocumentItemSql.CountActiveByTitle,
            DocumentSqlParameters.Create(("Title", title), ("CategoryId", categoryId), ("ExcludeId", excludedId)),
            cancellationToken).ConfigureAwait(false) > 0;
    }
}
