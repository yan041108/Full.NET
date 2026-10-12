using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

/// <summary>两条恢复入口共用分类与标题校验及 CAS 写入；事务和成功后的重读由调用方拥有。</summary>
internal sealed class DocumentItemRestoreService(IQueryExecutor queries, ICommandExecutor commands,
    DocumentItemCategoryReferenceService categories, IClock clock)
{
    public async Task<Result<bool>> RestoreAsync(Guid itemId, Guid actorUserId, long version, bool fromRecycleBin,
        CancellationToken cancellationToken)
    {
        var existing = await queries.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
            fromRecycleBin ? DocumentItemSql.FindDeletedById : DocumentItemSql.FindAnyById,
            DocumentSqlParameters.Create(("Id", itemId)), cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result<bool>.Failure(new Error(fromRecycleBin ? DocumentErrorCodes.RecycleItemNotFound : DocumentErrorCodes.NotFound,
                "Document item was not found.", ErrorType.NotFound));
        if (existing.DeletedAtUtc is null)
            return Conflict();

        // 失效分类不能复活为活动引用；降为未分类保持回收站恢复可用，同时防止删除/恢复竞争。
        var categoryId = await categories.LockActiveAsync(existing.CategoryId, cancellationToken).ConfigureAwait(false)
            ? existing.CategoryId : null;
        var collision = await queries.QuerySingleOrDefaultAsync<DocumentNameConflictRecord>(
            DocumentItemSql.FindActiveByTitle,
            DocumentSqlParameters.Create(("Title", existing.Title), ("CategoryId", categoryId), ("ExcludeId", itemId)),
            cancellationToken).ConfigureAwait(false);
        if (collision is not null)
            return Result<bool>.Failure(new Error(DocumentErrorCodes.TitleConflict,
                "A document with the same title already exists in this category.", ErrorType.Conflict));

        var affected = await commands.ExecuteAsync(DocumentItemSql.Restore,
            DocumentSqlParameters.Create(("Id", itemId), ("CategoryId", categoryId), ("UpdatedAtUtc", clock.UtcNow),
                ("UpdatedByUserId", actorUserId), ("Version", version)), cancellationToken).ConfigureAwait(false);
        return affected == 1 ? Result<bool>.Success(true) : Conflict();
    }

    private static Result<bool> Conflict() => Result<bool>.Failure(new Error(DocumentErrorCodes.VersionConflict,
        "Document item was updated by another operation.", ErrorType.Conflict));
}
