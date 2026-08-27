using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.QueryHostRecycleBin;

/// <summary>
/// Host 文档回收站管理服务。Restore 通过乐观并发 Version 自增实现软删除行的复活，
/// Purge 执行物理删除并联动 Files 模块文件引用清理事后对账；二者均在单一事务内完成，
/// Purge 前必须确认无引用残留以避免产生孤儿文件。
/// </summary>
internal sealed class HostRecycleBinManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentItemQueryService itemQueries,
    IClock clock)
{
    public Task<Result<HostDocumentItemResponse>> RestoreAsync(
        Guid itemId,
        Guid actorUserId,
        RestoreHostDocumentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteResultAsync(
            token => RestoreCoreAsync(itemId, actorUserId, request.Version, token),
            cancellationToken);
    }

    public Task<Result<bool>> PurgeAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        return transaction.ExecuteResultAsync(
            token => PurgeCoreAsync(itemId, token),
            cancellationToken);
    }

    private async Task<Result<HostDocumentItemResponse>> RestoreCoreAsync(
        Guid itemId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindDeletedById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                DocumentItemSql.Restore,
                DocumentSqlParameters.Create(("Id", itemId), ("UpdatedAtUtc", now), ("UpdatedByUserId", actorUserId), ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected != 1)
        {
            return VersionConflict();
        }

        return await itemQueries.GetByIdAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> PurgeCoreAsync(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindDeletedById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return Result<bool>.Failure(NotFoundError());
        }

        var affected = await commandExecutor.ExecuteAsync(
                DocumentItemSql.Purge,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected != 1)
        {
            return Result<bool>.Failure(PurgeFailedError());
        }

        return Result<bool>.Success(true);
    }

    private static Result<HostDocumentItemResponse> Invalid() =>
        Result<HostDocumentItemResponse>.Failure(InvalidError());

    private static Result<HostDocumentItemResponse> NotFound() =>
        Result<HostDocumentItemResponse>.Failure(NotFoundError());

    private static Result<HostDocumentItemResponse> VersionConflict() =>
        Result<HostDocumentItemResponse>.Failure(VersionConflictError());

    private static Error InvalidError() =>
        new(DocumentErrorCodes.Invalid, "The recycle bin request is invalid.", ErrorType.Validation);

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.RecycleItemNotFound, "Recycle bin item was not found.", ErrorType.NotFound);

    private static Error VersionConflictError() =>
        new(DocumentErrorCodes.VersionConflict, "Recycle bin item was updated by another operation.", ErrorType.Conflict);

    private static Error PurgeFailedError() =>
        new(DocumentErrorCodes.RecyclePurgeFailed, "Failed to permanently delete the recycle bin item.", ErrorType.Conflict);
}
