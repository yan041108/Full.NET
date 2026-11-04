using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

/// <summary>在调用方统一分类闸门内检查标题冲突，包含未分类与历史重复记录。</summary>
internal sealed class DocumentItemTitleService(IQueryExecutor queries)
{
    public async Task<bool> HasConflictAsync(string title, Guid? categoryId, Guid? excludedId, CancellationToken cancellationToken)
    {
        // 调用方持有统一分类事务闸门，包含未分类标题准入，不额外建立第二套锁顺序。
        // 历史数据可能已有多条同名记录；计数判冲突，不能假设查询最多返回一行。
        return await queries.QuerySingleOrDefaultAsync<long>(DocumentItemSql.CountActiveByTitle,
            DocumentSqlParameters.Create(("Title", title), ("CategoryId", categoryId), ("ExcludeId", excludedId)),
            cancellationToken).ConfigureAwait(false) > 0;
    }
}
