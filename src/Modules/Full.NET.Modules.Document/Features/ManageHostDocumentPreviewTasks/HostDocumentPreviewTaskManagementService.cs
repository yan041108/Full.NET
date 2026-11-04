using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Domain;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.Document.Security;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

/// <summary>创建时固定版本、文档归属及源/输出引用；所有 Files 调用都在本地事务外。</summary>
internal sealed class HostDocumentPreviewTaskManagementService(
    IQueryExecutor queryExecutor, ICommandExecutor commandExecutor, ICommandTransaction transaction,
    IHostFileContentReader hostFileContentReader, IClock clock, IIdGenerator idGenerator,
    IOptionsMonitor<DocumentOfficePreviewConversionOptions> options, IDocumentAccessPolicy accessPolicy,
    DocumentMutationGuard guard, DocumentPreviewLifecycle lifecycle)
{
    internal DocumentPreviewCapabilities Capabilities => new(options.CurrentValue.CanConvert, options.CurrentValue.ProviderKey,
        DocumentOfficePreviewSourcePolicy.AllowedMimeTypes, options.CurrentValue.MaxInputBytes, options.CurrentValue.MaxAttempts);

    public Task<Result<HostDocumentPreviewTaskResponse>> CreateAsync(CreateHostDocumentPreviewTaskRequest request, Guid requestedByUserId,
        CancellationToken cancellationToken = default) => accessPolicy.AuthorizeAsync(request.DocumentItemId, DocumentOperation.Read,
            () => CreateAuthorizedAsync(request, requestedByUserId, cancellationToken), cancellationToken);

    internal async Task<Result<HostDocumentPreviewTaskResponse>> CreateAuthorizedAsync(CreateHostDocumentPreviewTaskRequest request,
        Guid? requestedByUserId, CancellationToken cancellationToken = default, Guid? creationTaskId = null)
    {
        if (creationTaskId.HasValue && await lifecycle.FindAsync(creationTaskId.Value, cancellationToken).ConfigureAwait(false) is { } replay)
        {
            if (replay.DocumentItemId != request.DocumentItemId || replay.RequestedByUserId != requestedByUserId ||
                (request.VersionId.HasValue && replay.VersionId != request.VersionId)) return Conflict();
            return Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map(replay));
        }
        if (!options.CurrentValue.CanConvert) return Fail(DocumentErrorCodes.OfficePreviewProviderDisabled, ErrorType.BusinessRule);
        var item = await queryExecutor.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(DocumentItemSql.FindActiveById,
            DocumentSqlParameters.Create(("Id", request.DocumentItemId)), cancellationToken).ConfigureAwait(false);
        if (item is null || item.IsPurging) return NotFound();
        var versionId = request.VersionId ?? item.CurrentVersionId;
        if (versionId is null) return Fail(DocumentErrorCodes.NoCurrentVersion, ErrorType.NotFound);
        var version = await queryExecutor.QuerySingleOrDefaultAsync<DocumentVersionRecord>(DocumentItemSql.FindVersionById,
            DocumentSqlParameters.Create(("VersionId", versionId.Value), ("DocumentItemId", item.Id)), cancellationToken).ConfigureAwait(false);
        if (version is null) return NotFound();
        var probe = await hostFileContentReader.OpenReadyContentAsync(version.FileId, cancellationToken).ConfigureAwait(false);
        if (!probe.IsSuccess) return Result<HostDocumentPreviewTaskResponse>.Failure(probe.Error!);
        await using var source = probe.Value!.Content;
        if (!DocumentOfficePreviewSourcePolicy.IsOfficeMime(probe.Value.ContentType)) return Fail(DocumentErrorCodes.OfficePreviewUnsupportedSource, ErrorType.BusinessRule);
        if (source.CanSeek && source.Length > options.CurrentValue.MaxInputBytes) return Fail(DocumentErrorCodes.OfficePreviewInputTooLarge, ErrorType.BusinessRule);
        var taskId = creationTaskId ?? idGenerator.NewId();
        var sourceReferenceId = idGenerator.NewId();
        var outputReferenceId = idGenerator.NewId();
        var reservedOutputId = idGenerator.NewId();
        try
        {
            var inserted = await transaction.ExecuteResultAsync(async ct =>
            {
                var locked = await guard.LockAsync(item.Id, ct).ConfigureAwait(false);
                if (locked is null || locked.DeletedAtUtc is not null || locked.IsPurging) return NotFound();
                if (requestedByUserId.HasValue && await accessPolicy.CheckAsync(item.Id, DocumentOperation.Read, ct).ConfigureAwait(false) is { } denied)
                    return Result<HostDocumentPreviewTaskResponse>.Failure(denied);
                if (!request.VersionId.HasValue && locked.CurrentVersionId != versionId) return Conflict();
                var stillExists = await queryExecutor.QuerySingleOrDefaultAsync<DocumentVersionRecord>(DocumentItemSql.FindVersionById,
                    DocumentSqlParameters.Create(("VersionId", versionId.Value), ("DocumentItemId", item.Id)), ct).ConfigureAwait(false);
                if (stillExists is null || stillExists.FileId != version.FileId) return Conflict();
                var now = clock.UtcNow;
                foreach (var (id, file, purpose) in new[] { (sourceReferenceId, version.FileId, "source"), (outputReferenceId, reservedOutputId, "output") })
                    await commandExecutor.ExecuteAsync(DocumentPreviewReferenceSql.Insert, DocumentSqlParameters.Create(("Id", id), ("DocumentId", item.Id),
                        ("TaskId", taskId), ("FileId", file), ("Purpose", purpose), ("Now", now)), ct).ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(DocumentPreviewTaskSql.Insert, DocumentSqlParameters.Create(("Id", taskId), ("DocumentItemId", item.Id),
                    ("VersionId", versionId.Value), ("DocumentTitle", locked.Title), ("SourceFileId", version.FileId),
                    ("SourceFileName", probe.Value.OriginalFileName), ("SourceMimeType", probe.Value.ContentType), ("OutputFileId", null),
                    ("StatusKey", "preparing"), ("ProviderKey", options.CurrentValue.ProviderKey), ("ErrorCode", null), ("RequestedByUserId", requestedByUserId),
                    ("CreatedAtUtc", now), ("StartedAtUtc", null), ("CompletedAtUtc", null), ("Version", 1L),
                    ("SourceReferenceId", sourceReferenceId), ("OutputReferenceId", outputReferenceId), ("OutputOwnerId", locked.CreatedByUserId),
                    ("AttemptCount", 0), ("NextAttemptAtUtc", null), ("LeaseExpiresAtUtc", null)), ct).ConfigureAwait(false);
                var created = await lifecycle.FindAsync(taskId, ct).ConfigureAwait(false);
                return created is null ? NotFound() : Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map(created));
            }, cancellationToken).ConfigureAwait(false);
            if (!inserted.IsSuccess) return inserted;
        }
        catch (DataCommandException error) when (creationTaskId.HasValue && error.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return await CreateAuthorizedAsync(request, requestedByUserId, cancellationToken, creationTaskId).ConfigureAwait(false);
        }
        var task = await lifecycle.FindAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null) return NotFound();
        await lifecycle.PrepareAsync(task, cancellationToken).ConfigureAwait(false);
        var current = await lifecycle.FindAsync(taskId, cancellationToken).ConfigureAwait(false);
        return current is null ? NotFound() : Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map(current));
    }

    internal async Task<Result<HostDocumentPreviewTaskResponse>> CancelAsync(Guid taskId, long version, CancellationToken token)
    {
        var task = await lifecycle.FindAsync(taskId, token).ConfigureAwait(false);
        if (task is null) return NotFound();
        return await accessPolicy.AuthorizeAsync(task.DocumentItemId, DocumentOperation.Write, () => transaction.ExecuteResultAsync(async ct =>
        {
            var item = await guard.LockAsync(task.DocumentItemId, ct).ConfigureAwait(false);
            if (item is null || item.DeletedAtUtc is not null || item.IsPurging) return NotFound();
            if (await accessPolicy.CheckAsync(item.Id, DocumentOperation.Write, ct).ConfigureAwait(false) is { } denied) return Result<HostDocumentPreviewTaskResponse>.Failure(denied);
            var current = await lifecycle.FindAsync(taskId, ct).ConfigureAwait(false);
            if (current is null) return NotFound();
            if (version < 1 || current.Version != version) return Conflict();
            if (current.StatusKey == "cancelled") return Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map(current));
            var changed = await commandExecutor.ExecuteAsync(DocumentPreviewTaskSql.Cancel, DocumentSqlParameters.Create(("Id", taskId), ("Version", version), ("Now", clock.UtcNow)), ct).ConfigureAwait(false);
            if (changed != 1) return Conflict();
            await lifecycle.ReleaseTaskAsync(current, true, ct).ConfigureAwait(false);
            await lifecycle.FinishAttemptAsync(current, "document.preview.cancelled", ct).ConfigureAwait(false);
            return Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map((await lifecycle.FindAsync(taskId, ct).ConfigureAwait(false))!));
        }, token), token).ConfigureAwait(false);
    }

    internal async Task<Result<HostDocumentPreviewTaskResponse>> RetryAsync(Guid taskId, Guid actor, CancellationToken token)
    {
        var task = await lifecycle.FindAsync(taskId, token).ConfigureAwait(false);
        if (task is null) return NotFound();
        return await accessPolicy.AuthorizeAsync(task.DocumentItemId, DocumentOperation.Read, () =>
            task.StatusKey is not ("failed" or "cancelled") ? Task.FromResult(Conflict())
                : CreateAuthorizedAsync(new(task.DocumentItemId, task.VersionId), actor, token), token).ConfigureAwait(false);
    }

    private static Result<HostDocumentPreviewTaskResponse> NotFound() => Fail(DocumentErrorCodes.PreviewTaskNotFound, ErrorType.NotFound);
    private static Result<HostDocumentPreviewTaskResponse> Conflict() => Fail(DocumentErrorCodes.VersionConflict, ErrorType.Conflict);
    private static Result<HostDocumentPreviewTaskResponse> Fail(string code, ErrorType type) => Result<HostDocumentPreviewTaskResponse>.Failure(new Error(code, "Document preview request cannot be completed.", type));
}
