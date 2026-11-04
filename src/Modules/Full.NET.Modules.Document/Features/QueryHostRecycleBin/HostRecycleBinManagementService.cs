using Full.NET.Modules.Document.Security;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.QueryHostRecycleBin;

/// <summary>
/// Host 文档回收站管理服务。Restore 通过乐观并发 Version 自增实现软删除行的复活，
/// Purge 以有界事务移除版本并原子写入引用释放 Outbox；中断后可继续，Files 最终幂等释放。
/// </summary>
internal sealed class HostRecycleBinManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentItemQueryService itemQueries,
    IClock clock,
    Full.NET.Modules.Document.Features.DocumentCategoryGuard categoryGuard,
    Full.NET.Modules.Document.Features.ManageHostDocumentItems.DocumentItemRestoreService restores,
    IDocumentAccessPolicy accessPolicy,
    Full.NET.Modules.Document.Features.DocumentMutationGuard mutationGuard,
    Full.NET.Modules.Document.Features.DocumentReferenceReleasePublisher releasePublisher,
    IOptions<DatabaseOptions> database,
    Full.NET.Modules.Document.PreviewTasks.DocumentPreviewLifecycle previews, IIdGenerator idGenerator,
    IOptionsMonitor<Full.NET.Modules.Document.Configuration.DocumentOfficePreviewConversionOptions> previewOptions)
{
    public Task<Result<HostDocumentItemResponse>> RestoreAsync(
        Guid itemId,
        Guid actorUserId,
        RestoreHostDocumentItemRequest request,
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Write,
            () => RestoreAuthorizedAsync(itemId, actorUserId, request, cancellationToken), cancellationToken);

    private Task<Result<HostDocumentItemResponse>> RestoreAuthorizedAsync(
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
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Delete,
            () => PurgeAuthorizedAsync(itemId, cancellationToken), cancellationToken);

    private async Task<Result<bool>> PurgeAuthorizedAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await transaction.ExecuteResultAsync(token => PurgeCoreAsync(itemId, token), cancellationToken).ConfigureAwait(false);
            if (!batch.IsSuccess || batch.Value) return batch;
        }
    }

    private async Task<Result<HostDocumentItemResponse>> RestoreCoreAsync(
        Guid itemId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        await categoryGuard.LockAsync(cancellationToken).ConfigureAwait(false);
        var existing = await mutationGuard.LockAsync(itemId, cancellationToken).ConfigureAwait(false);

        if (existing is null || existing.DeletedAtUtc is null || existing.IsPurging)
        {
            return NotFound();
        }

        if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Write, cancellationToken).ConfigureAwait(false) is { } denied)
            return Result<HostDocumentItemResponse>.Failure(denied);
        var restored = await restores.RestoreAsync(existing, actorUserId, version, fromRecycleBin: true, cancellationToken).ConfigureAwait(false);
        if (!restored.IsSuccess) return Result<HostDocumentItemResponse>.Failure(restored.Error!);

        return await itemQueries.GetByIdAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> PurgeCoreAsync(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var existing = await mutationGuard.LockAsync(itemId, cancellationToken).ConfigureAwait(false);

        if (existing is null || existing.DeletedAtUtc is null)
        {
            return Result<bool>.Failure(NotFoundError());
        }

        if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Delete, cancellationToken).ConfigureAwait(false) is { } denied)
            return Result<bool>.Failure(denied);
        await commandExecutor.ExecuteAsync(DocumentItemSql.MarkPurging, DocumentSqlParameters.Create(("Id", itemId)), cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(DocumentDeletionFactSql.Insert, DocumentSqlParameters.Create(("Id", idGenerator.NewId()),
            ("DocumentId", itemId), ("ActorUserId", accessPolicy.ActorUserId), ("Now", clock.UtcNow),
            ("RetainUntilUtc", clock.UtcNow.AddDays(previewOptions.CurrentValue.DeletionFactRetentionDays))), cancellationToken).ConfigureAwait(false);
        var references = await queryExecutor.QueryAsync<DocumentPreviewReferenceRecord>(database.Value.Provider == DatabaseProvider.MySql
            ? DocumentPreviewReferenceSql.ByDocumentMySql : DocumentPreviewReferenceSql.ByDocumentSqlServer,
            DocumentSqlParameters.Create(("DocumentId", itemId)), cancellationToken).ConfigureAwait(false);
        var leaseEnd = await queryExecutor.QuerySingleOrDefaultAsync<DateTimeOffset?>(DocumentPreviewReferenceSql.DocumentLeaseEnd,
            DocumentSqlParameters.Create(("DocumentId", itemId)), cancellationToken).ConfigureAwait(false) ?? clock.UtcNow;
        foreach (var reference in references)
            await previews.ReleaseReferenceAsync(reference, leaseEnd, cancellationToken).ConfigureAwait(false);
        if (references.Count == 100) return Result<bool>.Success(false);
        // 每批独立提交释放意图，宕机后仍可按文档标记继续，不持有无界事务。
        var versions = await queryExecutor.QueryAsync<DocumentVersionRecord>(
            database.Value.Provider == DatabaseProvider.MySql ? DocumentItemSql.PurgeVersionsMySql : DocumentItemSql.PurgeVersionsSqlServer,
            DocumentSqlParameters.Create(("DocumentItemId", itemId)), cancellationToken).ConfigureAwait(false);
        foreach (var version in versions)
        {
            await releasePublisher.PublishAsync(version.Id, version.FileId, cancellationToken).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(DocumentVersionRetentionSql.DeleteVersionById,
                DocumentSqlParameters.Create(("DocumentItemId", itemId), ("VersionId", version.Id)), cancellationToken).ConfigureAwait(false);
        }
        if (versions.Count == 100) return Result<bool>.Success(false);
        var remaining = false;
        foreach (var statement in database.Value.Provider == DatabaseProvider.MySql ? DocumentPurgeSql.MySql : DocumentPurgeSql.SqlServer)
        {
            var removed = await commandExecutor.ExecuteAsync(statement,
                DocumentSqlParameters.Create(("DocumentItemId", itemId), ("ActorUserId", accessPolicy.ActorUserId)), cancellationToken).ConfigureAwait(false);
            remaining |= removed == 100;
            if (removed == 100) return Result<bool>.Success(false);
        }
        if (remaining) return Result<bool>.Success(false);

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
