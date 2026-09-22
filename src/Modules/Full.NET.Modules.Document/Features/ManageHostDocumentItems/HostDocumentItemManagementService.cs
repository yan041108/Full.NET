using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

/// <summary>
/// Host 文档项与版本的管理服务。所有写操作必须在单一事务内完成，
/// 并通过乐观并发 Version 字段防止跨客户端覆盖；新增版本时必须先 Claim Files 模块的文件引用，
/// 写入失败回滚事务会同步释放引用，禁止在事务外做文件保留期回收以避免孤儿文件。
/// </summary>
internal sealed class HostDocumentItemManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IHostFileReferenceClaimService hostFileReferenceClaimService,
    IHostFileUploadWriter hostFileUploadWriter,
    DocumentVersionDeletionService documentVersionDeletionService,
    DocumentItemTagAssignmentService documentItemTagAssignmentService,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<HostDocumentItemResponse>> CreateAsync(
        Guid actorUserId,
        CreateHostDocumentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);
    }

    public Task<Result<HostDocumentItemResponse>> UpdateAsync(
        Guid itemId,
        Guid actorUserId,
        UpdateHostDocumentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(itemId, actorUserId, request, token),
            cancellationToken);
    }

    public async Task<Result<HostDocumentItemResponse>> AddVersionAsync(
        Guid itemId,
        Guid actorUserId,
        AddHostDocumentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FileId == Guid.Empty)
        {
            return Invalid();
        }

        var versionId = idGenerator.NewId();
        return await AddVersionWithClaimAsync(
                itemId,
                actorUserId,
                versionId,
                request.FileId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<HostDocumentItemResponse>> AddVersionFromUploadAsync(
        Guid itemId,
        Guid actorUserId,
        string originalFileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        if (contentLength <= 0)
        {
            return Invalid();
        }

        var uploadResult = await hostFileUploadWriter
            .UploadAsync(
                actorUserId,
                originalFileName,
                contentType,
                content,
                contentLength,
                cancellationToken)
            .ConfigureAwait(false);
        if (!uploadResult.IsSuccess)
        {
            return Result<HostDocumentItemResponse>.Failure(uploadResult.Error!);
        }

        var versionId = idGenerator.NewId();
        return await AddVersionWithClaimAsync(
                itemId,
                actorUserId,
                versionId,
                uploadResult.Value!.FileId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<bool>> DeleteAsync(
        Guid itemId,
        Guid actorUserId,
        DeleteHostDocumentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Result<bool>.Failure(InvalidError()));
        }

        return transaction.ExecuteResultAsync(
            token => DeleteCoreAsync(itemId, actorUserId, request.Version, token),
            cancellationToken);
    }

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

    /// <summary>将当前版本指针切换到既有历史版本，不修改版本行也不触发 Files Claim。</summary>
    /// <param name="itemId">目标文档项标识。</param>
    /// <param name="versionId">要恢复为当前版本的历史版本标识。</param>
    /// <param name="actorUserId">执行回滚的用户标识。</param>
    /// <param name="request">携带文档项乐观并发版本的请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<Result<HostDocumentItemResponse>> RollbackVersionAsync(
        Guid itemId,
        Guid versionId,
        Guid actorUserId,
        RollbackHostDocumentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteResultAsync(
            token => RollbackVersionCoreAsync(
                itemId,
                versionId,
                actorUserId,
                request.Version,
                token),
            cancellationToken);
    }

    /// <summary>删除非当前的历史版本，写入删除审计并释放 Files Claim。</summary>
    /// <param name="itemId">目标文档项标识。</param>
    /// <param name="versionId">待删除历史版本标识。</param>
    /// <param name="actorUserId">执行删除的用户标识。</param>
    /// <param name="request">携带文档项乐观并发版本的请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<Result<HostDocumentItemResponse>> DeleteVersionAsync(
        Guid itemId,
        Guid versionId,
        Guid actorUserId,
        DeleteHostDocumentVersionRequest request,
        CancellationToken cancellationToken = default) =>
        documentVersionDeletionService.DeleteVersionManuallyAsync(
            itemId,
            versionId,
            actorUserId,
            request,
            cancellationToken);

    private async Task<Result<HostDocumentItemResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateHostDocumentItemRequest request,
        CancellationToken cancellationToken)
    {
        if (await FindTitleConflictAsync(
                request.Title.Trim(),
                request.CategoryId,
                null,
                cancellationToken).ConfigureAwait(false))
        {
            return TitleConflict();
        }

        var tagIds = request.TagIds?.ToArray() ?? Array.Empty<Guid>();
        var tagValidation = await documentItemTagAssignmentService
            .ValidateTagIdsAsync(tagIds, cancellationToken)
            .ConfigureAwait(false);
        if (!tagValidation.IsSuccess)
        {
            return Result<HostDocumentItemResponse>.Failure(tagValidation.Error!);
        }

        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                DocumentItemSql.Insert,
                DocumentSqlParameters.Create(
                    ("Id", id),
                    ("CategoryId", request.CategoryId),
                    ("Title", request.Title.Trim()),
                    ("Description", request.Description?.Trim()),
                    ("DocumentType", (int)request.DocumentType),
                    ("Status", (int)request.Status),
                    ("Sort", request.Sort),
                    ("Thumbnail", request.Thumbnail),
                    ("CreatedAtUtc", now),
                    ("CreatedByUserId", actorUserId),
                    ("Version", 1L)),
                cancellationToken)
            .ConfigureAwait(false);

        if (tagIds.Length > 0)
        {
            await documentItemTagAssignmentService
                .ReplaceAssignmentsAsync(id, tagIds, now, cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReloadActiveAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostDocumentItemResponse>> UpdateCoreAsync(
        Guid itemId,
        Guid actorUserId,
        UpdateHostDocumentItemRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var categoryId = request.CategoryId ?? existing.CategoryId;
        if (await FindTitleConflictAsync(
                request.Title.Trim(),
                categoryId,
                itemId,
                cancellationToken).ConfigureAwait(false))
        {
            return TitleConflict();
        }

        if (request.TagIds is not null)
        {
            var tagValidation = await documentItemTagAssignmentService
                .ValidateTagIdsAsync(request.TagIds, cancellationToken)
                .ConfigureAwait(false);
            if (!tagValidation.IsSuccess)
            {
                return Result<HostDocumentItemResponse>.Failure(tagValidation.Error!);
            }
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                DocumentItemSql.Update,
                DocumentSqlParameters.Create(
                    ("Id", itemId),
                    ("Title", request.Title.Trim()),
                    ("Description", request.Description?.Trim()),
                    ("CategoryId", categoryId),
                    ("Thumbnail", request.Thumbnail ?? existing.Thumbnail),
                    ("Status", (int)(request.Status ?? (HostDocumentStatus)existing.Status)),
                    ("Sort", request.Sort ?? existing.Sort),
                    ("UpdatedAtUtc", now),
                    ("UpdatedByUserId", actorUserId),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionConflict();
        }

        if (request.TagIds is not null)
        {
            await documentItemTagAssignmentService
                .ReplaceAssignmentsAsync(itemId, request.TagIds, now, cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReloadActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostDocumentItemResponse>> AddVersionWithClaimAsync(
        Guid itemId,
        Guid actorUserId,
        Guid versionId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.DocumentVersion(versionId);
        var claimResult = await hostFileReferenceClaimService
            .ClaimAsync(
                new HostFileReferenceClaimRequest(
                    idempotencyKey,
                    HostFileReferenceClaimConsumerModules.Document,
                    versionId,
                    fileId),
                cancellationToken)
            .ConfigureAwait(false);
        if (!claimResult.IsSuccess)
        {
            return MapClaimFailure(claimResult.Error!);
        }

        Result<HostDocumentItemResponse> writeResult;
        try
        {
            writeResult = await transaction.ExecuteResultAsync(
                    token => AddVersionCoreAsync(
                        itemId,
                        actorUserId,
                        versionId,
                        claimResult.Value!.FileReference,
                        token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // 提交结果未知时保留 Pending claim，交给 Files 对账，不得同步释放。
            throw;
        }

        if (!writeResult.IsSuccess)
        {
            await hostFileReferenceClaimService
                .ReleaseAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            return writeResult;
        }

        _ = await hostFileReferenceClaimService
            .ConfirmAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        return writeResult;
    }

    private async Task<Result<HostDocumentItemResponse>> AddVersionCoreAsync(
        Guid itemId,
        Guid actorUserId,
        Guid versionId,
        HostFileReference fileReference,
        CancellationToken cancellationToken)
    {
        var item = await FindActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return NotFound();
        }

        var versionNumber = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                DocumentItemSql.NextVersionNumber,
                DocumentSqlParameters.Create(("DocumentItemId", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                DocumentItemSql.InsertVersion,
                DocumentSqlParameters.Create(("Id", versionId), ("DocumentItemId", itemId), ("FileId", fileReference.FileId), ("VersionNumber", versionNumber), ("ContentHash", fileReference.ContentHash), ("SizeBytes", fileReference.SizeBytes), ("UploadedByUserId", actorUserId), ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var affected = await commandExecutor.ExecuteAsync(
                DocumentItemSql.SetCurrentVersion,
                DocumentSqlParameters.Create(("Id", itemId), ("CurrentVersionId", versionId), ("UpdatedAtUtc", now), ("UpdatedByUserId", actorUserId), ("Version", item.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionConflict();
        }

        return await ReloadActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid itemId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        if (await FindActiveAsync(itemId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result<bool>.Failure(NotFoundError());
        }

        var affected = await commandExecutor.ExecuteAsync(
                DocumentItemSql.SoftDelete,
                DocumentSqlParameters.Create(("Id", itemId), ("DeletedAtUtc", clock.UtcNow), ("DeletedByUserId", actorUserId), ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(VersionConflictError());
    }

    private async Task<Result<HostDocumentItemResponse>> RestoreCoreAsync(
        Guid itemId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindAnyById,
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

        return await ReloadActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostDocumentItemResponse>> RollbackVersionCoreAsync(
        Guid itemId,
        Guid versionId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        var item = await FindActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return NotFound();
        }

        if (item.CurrentVersionId == versionId)
        {
            return VersionAlreadyCurrent();
        }

        var targetVersion = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentVersionRecord>(
                DocumentItemSql.FindVersionById,
                DocumentSqlParameters.Create(
                    ("VersionId", versionId),
                    ("DocumentItemId", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (targetVersion is null)
        {
            return VersionNotFound();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                DocumentItemSql.SetCurrentVersion,
                DocumentSqlParameters.Create(
                    ("Id", itemId),
                    ("CurrentVersionId", versionId),
                    ("UpdatedAtUtc", now),
                    ("UpdatedByUserId", actorUserId),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionConflict();
        }

        return await ReloadActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    private Task<DocumentItemDetailRecord?> FindActiveAsync(
        Guid itemId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
            DocumentItemSql.FindActiveById,
            DocumentSqlParameters.Create(("Id", itemId)),
            cancellationToken);

    private async Task<Result<HostDocumentItemResponse>> ReloadActiveAsync(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var record = await FindActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var tagsByItem = await documentItemTagAssignmentService
            .ListByDocumentItemIdsAsync([itemId], cancellationToken)
            .ConfigureAwait(false);
        tagsByItem.TryGetValue(itemId, out var tags);
        return Result<HostDocumentItemResponse>.Success(Map(record, tags));
    }

    private async Task<bool> FindTitleConflictAsync(
        string title,
        Guid? categoryId,
        Guid? excludeItemId,
        CancellationToken cancellationToken)
    {
        var conflict = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentNameConflictRecord>(
                DocumentItemSql.FindActiveByTitle,
                DocumentSqlParameters.Create(
                    ("Title", title),
                    ("CategoryId", categoryId),
                    ("ExcludeId", excludeItemId)),
                cancellationToken)
            .ConfigureAwait(false);
        return conflict is not null;
    }

    private static HostDocumentItemResponse Map(
        DocumentItemDetailRecord record,
        IReadOnlyList<HostDocumentTagAssignmentResponse>? tags = null) =>
        HostDocumentItemResponseMapper.Map(record, tags);

    private static Result<HostDocumentItemResponse> MapClaimFailure(Error error) =>
        string.Equals(error.Code, FilesErrorCodes.FileNotFound, StringComparison.Ordinal)
            ? InvalidFileReference()
            : Result<HostDocumentItemResponse>.Failure(error);

    private static Result<HostDocumentItemResponse> Invalid() =>
        Result<HostDocumentItemResponse>.Failure(InvalidError());

    private static Result<HostDocumentItemResponse> NotFound() =>
        Result<HostDocumentItemResponse>.Failure(NotFoundError());

    private static Result<HostDocumentItemResponse> VersionConflict() =>
        Result<HostDocumentItemResponse>.Failure(VersionConflictError());

    private static Result<HostDocumentItemResponse> TitleConflict() =>
        Result<HostDocumentItemResponse>.Failure(TitleConflictError());

    private static Result<HostDocumentItemResponse> VersionAlreadyCurrent() =>
        Result<HostDocumentItemResponse>.Failure(new Error(
            DocumentErrorCodes.VersionAlreadyCurrent,
            "The selected version is already the current version.",
            ErrorType.Conflict));

    private static Result<HostDocumentItemResponse> VersionNotFound() =>
        Result<HostDocumentItemResponse>.Failure(new Error(
            DocumentErrorCodes.VersionNotFound,
            "The document version was not found.",
            ErrorType.NotFound));

    private static Result<HostDocumentItemResponse> InvalidFileReference() =>
        Result<HostDocumentItemResponse>.Failure(
            new Error(
                DocumentErrorCodes.InvalidFileReference,
                "The referenced file is unavailable.",
                ErrorType.Validation));

    private static Error InvalidError() =>
        new(DocumentErrorCodes.Invalid, "The document item request is invalid.", ErrorType.Validation);

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.NotFound, "Document item was not found.", ErrorType.NotFound);

    private static Error TitleConflictError() =>
        new(
            DocumentErrorCodes.TitleConflict,
            "A document with the same title already exists in this category.",
            ErrorType.Conflict);

    private static Error VersionConflictError() =>
        new(DocumentErrorCodes.VersionConflict, "Document item was updated by another operation.", ErrorType.Conflict);
}
