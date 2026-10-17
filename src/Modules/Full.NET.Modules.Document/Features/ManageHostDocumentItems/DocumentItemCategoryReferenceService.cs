using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

/// <summary>在调用方事务内验证并锁定分类；无分类是合法状态，失效或非 Host 分类不能挂接。</summary>
internal sealed class DocumentItemCategoryReferenceService(IQueryExecutor queries, IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<bool> LockActiveAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null) return true;
        if (categoryId == Guid.Empty) return false;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentCategorySql.LockActiveByIdSqlServer,
            DatabaseProvider.MySql => DocumentCategorySql.LockActiveByIdMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var category = await queries.QuerySingleOrDefaultAsync<DocumentCategoryRecord>(statement,
            DocumentSqlParameters.Create(("Id", categoryId.Value)), cancellationToken).ConfigureAwait(false);
        return category is not null;
    }
}
