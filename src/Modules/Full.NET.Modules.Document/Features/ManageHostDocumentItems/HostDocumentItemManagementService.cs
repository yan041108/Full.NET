using Full.NET.Modules.Document.Security;
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
/// Host 文档项与版本的管理服务。文档写入在根行锁和本地事务内串行化，
/// 以 Version 防止跨客户端覆盖；Files Claim 在事务外申请，提交后确认，失败由补偿与对账释放。
/// </summary>
internal sealed class HostDocumentItemManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    DocumentCategoryGuard categoryGuard,
    DocumentItemRestoreService restores,
    IHostFileReferenceClaimService hostFileReferenceClaimService,
    IHostFileUploadWriter hostFileUploadWriter,
    DocumentVersionDeletionService documentVersionDeletionService,
    DocumentItemTagAssignmentService documentItemTagAssignmentService,
    IClock clock,
    IIdGenerator idGenerator,
    IDocumentAccessPolicy accessPolicy,
    DocumentMutationGuard mutationGuard,
    IHostFileDescriptorReader descriptorReader)
{
    public Task<Result<HostDocumentItemResponse>> CreateAsync(
        Guid actorUserId,
        CreateHostDocumentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Status != HostDocumentStatus.Draft || !Enum.IsDefined(request.DocumentType))
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
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Write,
            () => UpdateAuthorizedAsync(itemId, actorUserId, request, cancellationToken), cancellationToken);

    private Task<Result<HostDocumentItemResponse>> UpdateAuthorizedAsync(
        Guid itemId,
        Guid actorUserId,
        UpdateHostDocumentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Version < 1 || request.TagIds is null)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(itemId, actorUserId, request, token),
            cancellationToken);
    }

    public Task<Result<HostDocumentItemResponse>> AddVersionAsync(
        Guid itemId,
        Guid actorUserId,
        AddHostDocumentVersionRequest request,
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Write,
            () => AddVersionAuthorizedAsync(itemId, actorUserId, request, cancellationToken), cancellationToken);

    private async Task<Result<HostDocumentItemResponse>> AddVersionAuthorizedAsync(
        Guid itemId,
        Guid actorUserId,
        AddHostDocumentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FileId == Guid.Empty || request.RequestKey == Guid.Empty)
        {
            return Invalid();
        }

        var versionId = idGenerator.NewId();
        return await AddVersionWithClaimAsync(
                itemId,
                actorUserId,
                versionId,
                request.FileId,
                request.ChangeDescription,
                request.RequestKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<HostDocumentItemResponse>> AddVersionFromUploadAsync(
        Guid itemId,
        Guid actorUserId,
        string originalFileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default, Guid requestKey = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Write,
            () => AddVersionFromUploadAuthorizedAsync(itemId, actorUserId, originalFileName, contentType, content, contentLength, cancellationToken, requestKey), cancellationToken);

    private async Task<Result<HostDocumentItemResponse>> AddVersionFromUploadAuthorizedAsync(
        Guid itemId,
        Guid actorUserId,
        string originalFileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default, Guid requestKey = default)
    {
        if (contentLength <= 0 || requestKey == Guid.Empty)
        {
            return Invalid();
        }

        // 提前拒绝不可写状态，避免明知归档或删除仍发布外部文件；事务内仍须重查。
        var item = await queryExecutor.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(DocumentItemSql.FindActiveById,
            DocumentSqlParameters.Create(("Id", itemId)), cancellationToken).ConfigureAwait(false);
        if (item is null || item.DeletedAtUtc is not null) return NotFound();
        if (item.Status == (int)HostDocumentStatus.Archived) return StateConflict();

        var uploadResult = await hostFileUploadWriter
            .UploadIdempotentAsync(
                $"document-version:{itemId:N}:{actorUserId:N}:{requestKey:N}",
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
                null,
                requestKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<bool>> DeleteAsync(
        Guid itemId,
        Guid actorUserId,
        DeleteHostDocumentItemRequest request,
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Delete,
            () => DeleteAuthorizedAsync(itemId, actorUserId, request, cancellationToken), cancellationToken);

    private Task<Result<bool>> DeleteAuthorizedAsync(
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
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Write,
            () => RollbackVersionAuthorizedAsync(itemId, versionId, actorUserId, request, cancellationToken), cancellationToken);

    private Task<Result<HostDocumentItemResponse>> RollbackVersionAuthorizedAsync(
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
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Delete,
            () => DeleteVersionAuthorizedAsync(itemId, versionId, actorUserId, request, cancellationToken), cancellationToken);

    private Task<Result<HostDocumentItemResponse>> DeleteVersionAuthorizedAsync(
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
        await categoryGuard.LockAsync(cancellationToken).ConfigureAwait(false);
        if (request.CategoryId is Guid category && await queryExecutor.QuerySingleOrDefaultAsync<DocumentCategoryRecord>(
            DocumentCategorySql.FindActiveById, DocumentSqlParameters.Create(("Id", category)), cancellationToken).ConfigureAwait(false) is null)
            return Result<HostDocumentItemResponse>.Failure(new Error(DocumentErrorCodes.CategoryInvalidParent,
                "Category is unavailable.", ErrorType.Validation));

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
                    ("DocumentNo", "DOC-" + id.ToString("N")),
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
        await categoryGuard.LockAsync(cancellationToken).ConfigureAwait(false);
        if (request.CategoryId is Guid category && await queryExecutor.QuerySingleOrDefaultAsync<DocumentCategoryRecord>(
            DocumentCategorySql.FindActiveById, DocumentSqlParameters.Create(("Id", category)), cancellationToken).ConfigureAwait(false) is null)
            return Result<HostDocumentItemResponse>.Failure(new Error(DocumentErrorCodes.CategoryInvalidParent,
                "Category is unavailable.", ErrorType.Validation));

        var existing = await mutationGuard.LockAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Write, cancellationToken).ConfigureAwait(false) is { } denied) return Result<HostDocumentItemResponse>.Failure(denied);
        if (existing?.Status == (int)HostDocumentStatus.Archived) return StateConflict();
        if (existing is null || existing.DeletedAtUtc is not null)
        {
            return NotFound();
        }

        var categoryId = request.CategoryId;
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
                    ("Thumbnail", request.Thumbnail),
                    ("Sort", request.Sort),
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
        string? changeDescription,
        Guid requestKey,
        CancellationToken cancellationToken)
    {
        var replay = await queryExecutor.QuerySingleOrDefaultAsync<DocumentVersionOperationRecord>(DocumentVersionOperationSql.Find,
            DocumentSqlParameters.Create(("RequestKey", requestKey), ("DocumentId", itemId), ("ActorUserId", actorUserId)), cancellationToken).ConfigureAwait(false);
        if (replay is not null)
        {
            if (replay.DocumentId != itemId || replay.ActorUserId != actorUserId || replay.FileId != fileId || replay.ChangeDescription != changeDescription) return VersionConflict();
            var loaded = await ReloadActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
            return loaded.IsSuccess ? Result<HostDocumentItemResponse>.Success(loaded.Value! with { AppliedVersionId = replay.VersionId }) : loaded;
        }
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

        var descriptor = await descriptorReader.GetReadyDescriptorAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (descriptor is null || descriptor.SizeBytes != claimResult.Value!.FileReference.SizeBytes || descriptor.ContentHash != claimResult.Value.FileReference.ContentHash)
            return InvalidFileReference();
        Result<HostDocumentItemResponse> writeResult;
        try
        {
            writeResult = await transaction.ExecuteResultAsync(
                    token => AddVersionCoreAsync(
                        itemId,
                        actorUserId,
                        versionId,
                        claimResult.Value!.FileReference,
                        descriptor,
                        changeDescription,
                        requestKey,
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
            // 无法证明未被并发提交时保留 Pending，由精确 Probe 对账处理。
            return writeResult;
        }

        if (writeResult.Value!.AppliedVersionId is null || writeResult.Value.AppliedVersionId == versionId)
            _ = await hostFileReferenceClaimService.ConfirmAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);

        return writeResult.IsSuccess ? Result<HostDocumentItemResponse>.Success(writeResult.Value! with { AppliedVersionId = writeResult.Value.AppliedVersionId ?? versionId }) : writeResult;
    }

    private async Task<Result<HostDocumentItemResponse>> AddVersionCoreAsync(
        Guid itemId,
        Guid actorUserId,
        Guid versionId,
        HostFileReference fileReference,
        HostFileDescriptor descriptor,
        string? changeDescription,
        Guid requestKey,
        CancellationToken cancellationToken)
    {
        var item = await mutationGuard.LockAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Write, cancellationToken).ConfigureAwait(false) is { } denied) return Result<HostDocumentItemResponse>.Failure(denied);
        if (item?.Status == (int)HostDocumentStatus.Archived) return StateConflict();
        if (item is null || item.DeletedAtUtc is not null)
        {
            return NotFound();
        }

        var replay = await queryExecutor.QuerySingleOrDefaultAsync<DocumentVersionOperationRecord>(DocumentVersionOperationSql.Find,
            DocumentSqlParameters.Create(("RequestKey", requestKey), ("DocumentId", itemId), ("ActorUserId", actorUserId)), cancellationToken).ConfigureAwait(false);
        if (replay is not null)
        {
            if (replay.DocumentId != itemId || replay.ActorUserId != actorUserId || replay.FileId != fileReference.FileId || replay.ChangeDescription != changeDescription) return VersionConflict();
            var previous = await ReloadActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
            return previous.IsSuccess ? Result<HostDocumentItemResponse>.Success(previous.Value! with { AppliedVersionId = replay.VersionId }) : previous;
        }
        var versionNumber = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                DocumentItemSql.NextVersionNumber,
                DocumentSqlParameters.Create(("DocumentItemId", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(DocumentItemSql.AllocateVersion, DocumentSqlParameters.Create(("Id", itemId)), cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                DocumentItemSql.InsertVersion,
                DocumentSqlParameters.Create(("Id", versionId), ("DocumentItemId", itemId), ("FileId", fileReference.FileId), ("VersionNumber", versionNumber), ("ContentHash", fileReference.ContentHash), ("SizeBytes", fileReference.SizeBytes), ("FileName", descriptor.OriginalFileName), ("MimeType", descriptor.ContentType), ("Extension", Path.GetExtension(descriptor.OriginalFileName).ToLowerInvariant()), ("ChangeDescription", changeDescription), ("UploadedByUserId", actorUserId), ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        await commandExecutor.ExecuteAsync(DocumentVersionOperationSql.Insert, DocumentSqlParameters.Create(
            ("Id", idGenerator.NewId()), ("RequestKey", requestKey), ("DocumentId", itemId), ("ActorUserId", actorUserId), ("VersionId", versionId),
            ("FileId", fileReference.FileId), ("ChangeDescription", changeDescription), ("CreatedAtUtc", now)), cancellationToken).ConfigureAwait(false);
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
        var deleted = await mutationGuard.LockAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Delete, cancellationToken).ConfigureAwait(false) is { } denied) return Result<bool>.Failure(denied);
        if (deleted is null || deleted.DeletedAtUtc is not null)
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
        await categoryGuard.LockAsync(cancellationToken).ConfigureAwait(false);
        var existing = await mutationGuard.LockAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Write, cancellationToken).ConfigureAwait(false) is { } denied)
            return Result<HostDocumentItemResponse>.Failure(denied);
        if (existing is null || existing.DeletedAtUtc is null || existing.IsPurging)
        {
            return NotFound();
        }

        var restored = await restores.RestoreAsync(existing, actorUserId, version, fromRecycleBin: false, cancellationToken).ConfigureAwait(false);
        if (!restored.IsSuccess) return Result<HostDocumentItemResponse>.Failure(restored.Error!);

        return await ReloadActiveAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostDocumentItemResponse>> RollbackVersionCoreAsync(
        Guid itemId,
        Guid versionId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        var item = await mutationGuard.LockAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Write, cancellationToken).ConfigureAwait(false) is { } denied) return Result<HostDocumentItemResponse>.Failure(denied);
        if (item?.Status == (int)HostDocumentStatus.Archived) return StateConflict();
        if (item is null || item.DeletedAtUtc is not null)
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
        return await queryExecutor.QuerySingleOrDefaultAsync<long>(DocumentItemSql.CountActiveByTitle,
            DocumentSqlParameters.Create(("Title", title), ("CategoryId", categoryId), ("ExcludeId", excludeItemId)), cancellationToken).ConfigureAwait(false) > 0;
    }

    private static HostDocumentItemResponse Map(
        DocumentItemDetailRecord record,
        IReadOnlyList<HostDocumentTagAssignmentResponse>? tags = null) =>
        HostDocumentItemResponseMapper.Map(record, tags);

    /// <summary>生命周期转换在根锁内提交并撤销旧分享会话。</summary>
    public Task<Result<HostDocumentItemResponse>> ChangeStatusAsync(Guid itemId, Guid actorUserId,
        ChangeHostDocumentStatusRequest request, HostDocumentStatus target, CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(async token =>
        {
            var item = await mutationGuard.LockAsync(itemId, token).ConfigureAwait(false);
            if (item is null || item.DeletedAtUtc is not null) return NotFound();
            if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Write, token).ConfigureAwait(false) is { } denied)
                return Result<HostDocumentItemResponse>.Failure(denied);
            var expected = target switch { HostDocumentStatus.Published => HostDocumentStatus.Draft,
                HostDocumentStatus.Archived => HostDocumentStatus.Published, _ => HostDocumentStatus.Archived };
            if (item.Version != request.Version || item.Status != (int)expected ||
                (target == HostDocumentStatus.Published && item.CurrentVersionId is null)) return StateConflict();
            await commandExecutor.ExecuteAsync(DocumentItemSql.ChangeStatus,
                DocumentSqlParameters.Create(("Id", itemId), ("Status", (int)target), ("Version", request.Version),
                    ("Now", clock.UtcNow), ("ActorUserId", actorUserId)), token).ConfigureAwait(false);
            if (target == HostDocumentStatus.Archived)
                await commandExecutor.ExecuteAsync(DocumentItemSql.DisableShares, DocumentSqlParameters.Create(("Id", itemId)), token).ConfigureAwait(false);
            return await ReloadActiveAsync(itemId, token).ConfigureAwait(false);
        }, cancellationToken);

    private static Result<HostDocumentItemResponse> StateConflict() => Result<HostDocumentItemResponse>.Failure(
        new Error("document.host_document.state_conflict", "Document state does not allow this operation.", ErrorType.Conflict));

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
