using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>分类与标签目录的事务门禁；空目录也能保护名称准入及引用校验。</summary>
internal sealed class DocumentTaxonomyWriteGuard(IQueryExecutor queries, IOptions<DatabaseOptions> options)
{
    // 固定应用端 UUID v7 与迁移 251 一致；两个目录分别互斥，API 不补播技术行。
    internal static readonly Guid CategoryGuardId = Guid.Parse("01a12ef8-d360-7d89-91f6-7e58ddbfcd73");
    internal static readonly Guid TagGuardId = Guid.Parse("01a12ef8-d36c-78d9-b24b-93aa39be0747");

    public Task LockCategoriesAsync(CancellationToken cancellationToken) => LockAsync(CategoryGuardId, cancellationToken);
    public Task LockTagsAsync(CancellationToken cancellationToken) => LockAsync(TagGuardId, cancellationToken);

    private async Task LockAsync(Guid id, CancellationToken cancellationToken)
    {
        var statement = options.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentTaxonomySql.LockWriteSqlServer,
            DatabaseProvider.MySql => DocumentTaxonomySql.LockWriteMySql,
            _ => throw new InvalidOperationException("Document catalog locking requires SQL Server or MySQL."),
        };
        var acquired = await queries.QuerySingleOrDefaultAsync<int>(statement,
            DocumentSqlParameters.Create(("Id", id)), cancellationToken).ConfigureAwait(false);
        if (acquired != 1)
            throw new InvalidOperationException("Document catalog write guard is missing; run the database migrator before accepting writes.");
    }
}
