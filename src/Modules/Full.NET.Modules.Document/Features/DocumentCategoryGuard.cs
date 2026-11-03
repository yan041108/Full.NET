using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features;

/// <summary>
/// 分类树与引用写入的事务闸门。调用方必须在事务内、首次读取前取得此锁，
/// 再锁文档根行；持有到提交或回滚，保证跨实例互移、删除与引用创建不能穿透校验。
/// </summary>
internal sealed class DocumentCategoryGuard(ICommandExecutor commands)
{
    internal async Task LockAsync(CancellationToken token)
    {
        var affected = await commands.ExecuteAsync(DocumentCategorySql.LockTree,
            DocumentSqlParameters.Create(("Id", Guid.Parse("019a1900-0000-7000-8000-000000000001"))), token).ConfigureAwait(false);
        if (affected != 1) throw new InvalidOperationException("Document category guard is missing.");
    }
}
