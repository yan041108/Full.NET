using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Files.Contracts;
using static Full.NET.Modules.Ai.Features.ManageKnowledgeVersions.AiKnowledgeImportStore;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;

/// <summary>日志、上传、Claim、AI 提交和 Confirm 分段执行；任何外部操作都不借用 AI 事务。</summary>
internal sealed class AiKnowledgeImportCoordinator(AiKnowledgeImportStore store, IHostFileUploadWriter hostUploads,
    IHostFileReferenceClaimService claims, ITenantResourceFileStore tenantFiles, ITenantResourceFileCleanup tenantCleanup)
{
    internal async Task<Result<AiKnowledgeImportResponse>> UploadAsync(Guid knowledgeBaseId, Guid documentId, Guid actor,
        int version, Guid requestKey, string name, string type, Stream content, long? declaredBytes, CancellationToken token)
    {
        if (await store.OwnedDocumentAsync(knowledgeBaseId, documentId, actor, false, token).ConfigureAwait(false) is null)
            return NotFound<AiKnowledgeImportResponse>();
        if (AiKnowledgeFilePolicy.Validate(name, type, version, requestKey, declaredBytes) is { } invalid)
            return Failure<AiKnowledgeImportResponse>("ai.knowledge.upload_invalid", invalid, ErrorType.Validation);
        var buffered = await AiKnowledgeUploadBuffer.ReadAsync(content, type, token).ConfigureAwait(false);
        if (!buffered.IsSuccess) return Result<AiKnowledgeImportResponse>.Failure(buffered.Error!);
        using var payload = buffered.Value!;
        var started = await store.StartAsync(knowledgeBaseId, documentId, actor, version, requestKey, name, type, payload, token).ConfigureAwait(false);
        if (!started.IsSuccess) return Result<AiKnowledgeImportResponse>.Failure(started.Error!);
        var parameters = store.Parameters(knowledgeBaseId, documentId, actor);
        parameters.Add("OperationId", started.Value!.Id);
        return await RunAsync(parameters, payload, token).ConfigureAwait(false);
    }

    internal async Task<Result<AiKnowledgeImportResponse>> GetAsync(Guid knowledgeBaseId, Guid documentId, Guid operationId,
        Guid actor, CancellationToken token)
    {
        if (operationId == Guid.Empty || knowledgeBaseId == Guid.Empty || documentId == Guid.Empty) return NotFound<AiKnowledgeImportResponse>();
        var parameters = store.Parameters(knowledgeBaseId, documentId, actor);
        parameters.Add("OperationId", operationId);
        var row = await store.FindAsync(parameters, token).ConfigureAwait(false);
        return row is null ? NotFound<AiKnowledgeImportResponse>() : Result<AiKnowledgeImportResponse>.Success(Map(row));
    }

    internal async Task<Result<AiKnowledgeImportResponse>> RetryAsync(Guid knowledgeBaseId, Guid documentId, Guid operationId,
        Guid actor, CancellationToken token)
    {
        if (operationId == Guid.Empty || knowledgeBaseId == Guid.Empty || documentId == Guid.Empty) return NotFound<AiKnowledgeImportResponse>();
        var parameters = store.Parameters(knowledgeBaseId, documentId, actor);
        parameters.Add("OperationId", operationId);
        return await RunAsync(parameters, null, token).ConfigureAwait(false);
    }

    internal async Task<Result<int>> CleanupDocumentAsync(Guid knowledgeBaseId, Guid documentId, Guid actor, CancellationToken token)
    {
        if (await store.OwnedDocumentAsync(knowledgeBaseId, documentId, actor, true, token).ConfigureAwait(false) is null)
            return NotFound<int>();
        var rows = await store.CleanupRowsAsync(store.Parameters(knowledgeBaseId, documentId, actor), token).ConfigureAwait(false);
        var completed = 0;
        foreach (var row in rows)
        {
            var result = await RetryAsync(knowledgeBaseId, documentId, row.Id, actor, token).ConfigureAwait(false);
            if (result.IsSuccess && result.Value!.State == "failed") completed++;
        }
        return Result<int>.Success(completed);
    }

    private async Task<Result<AiKnowledgeImportResponse>> RunAsync(Dictionary<string, object?> parameters,
        AiKnowledgeUploadBuffer? payload, CancellationToken token)
    {
        var row = await store.FindAsync(parameters, token).ConfigureAwait(false);
        if (row is null) return NotFound<AiKnowledgeImportResponse>();
        if (row.StateKey is "completed" or "failed") return Result<AiKnowledgeImportResponse>.Success(Map(row));
        if (!await store.AcquireAsync(parameters, token).ConfigureAwait(false))
            return Failure<AiKnowledgeImportResponse>("ai.knowledge.import_busy", "The import is held by another bounded lease.", ErrorType.Conflict);
        try
        {
            row = (await store.FindAsync(parameters, token).ConfigureAwait(false))!;
            if (row.StateKey == "cleanup_pending") return await CleanupAsync(row, parameters, token).ConfigureAwait(false);
            if (await store.OwnedDocumentAsync((Guid)parameters["KnowledgeBaseId"]!, row.DocumentId, row.ActorUserId, false, token).ConfigureAwait(false) is null)
                return NotFound<AiKnowledgeImportResponse>();
            if (row.FileId is null)
            {
                var recovered = await RecoverTenantFileAsync(row, token).ConfigureAwait(false);
                Guid fileId;
                if (recovered is { } recoveredId) fileId = recoveredId;
                else if (payload is null)
                    return Failure<AiKnowledgeImportResponse>("ai.knowledge.upload_required", "Repeat the original bounded upload with the same retry key.", ErrorType.BusinessRule);
                else
                {
                    payload.Content.Position = 0;
                    if (store.IsTenant)
                    {
                        var upload = await tenantFiles.UploadAsync("ai", row.VersionId, row.ActorUserId, row.OriginalFileName,
                            row.ContentType, payload.Content, payload.Size, token).ConfigureAwait(false);
                        if (!upload.IsSuccess) return await KnownFailureAsync(row, parameters, upload.Error!, token).ConfigureAwait(false);
                        fileId = upload.Value!.FileId;
                        if (!Matches(row, upload.Value.SizeBytes, upload.Value.ContentHash))
                            return await RejectUploadedAsync(row, parameters, fileId, token).ConfigureAwait(false);
                    }
                    else
                    {
                        var upload = await hostUploads.UploadAsync(row.ActorUserId, row.OriginalFileName, row.ContentType,
                            payload.Content, payload.Size, token).ConfigureAwait(false);
                        if (!upload.IsSuccess) return await KnownFailureAsync(row, parameters, upload.Error!, token).ConfigureAwait(false);
                        fileId = upload.Value!.FileId;
                        if (!Matches(row, upload.Value.SizeBytes, upload.Value.ContentHash))
                            return await RejectUploadedAsync(row, parameters, fileId, token).ConfigureAwait(false);
                    }
                }
                // 过期执行者不能覆盖新租约，也不能释放可能已被新执行者持有的文件。
                if (fileId == Guid.Empty || !await store.SaveFileAsync(parameters, fileId, token).ConfigureAwait(false))
                    return Conflict<AiKnowledgeImportResponse>();
                row = (await store.FindAsync(parameters, token).ConfigureAwait(false))!;
            }
            if (row.StateKey == "cleanup_pending") return await CleanupAsync(row, parameters, token).ConfigureAwait(false);
            if (row.StateKey == "file_uploaded")
            {
                if (!store.IsTenant)
                {
                    var claim = await claims.ClaimAsync(new(HostFileReferenceClaimIdempotencyKeys.AiKnowledgeVersion(row.VersionId),
                        HostFileReferenceClaimConsumerModules.Ai, row.VersionId, row.FileId!.Value), token).ConfigureAwait(false);
                    if (!claim.IsSuccess) return await KnownFailureAsync(row, parameters, claim.Error!, token).ConfigureAwait(false);
                    if (claim.Value!.FileReference.FileId != row.FileId || !Matches(row, claim.Value.FileReference.SizeBytes, claim.Value.FileReference.ContentHash))
                        return await KnownFailureAsync(row, parameters, new Error("ai.knowledge.source_unavailable", "The file reference did not match.", ErrorType.BusinessRule), token).ConfigureAwait(false);
                }
                // 异常表示提交结果未知：不改为补偿阶段，不释放引用，后续读取权威日志再判断。
                var published = await store.PublishAsync(parameters, token).ConfigureAwait(false);
                // 禁用目录只拒绝访问，不能以此为由永久释放文件；删除已有独立补偿日志。
                if (!published.IsSuccess && published.Error!.Code == AiKnowledgeErrorCodes.DocumentNotFound)
                    return Result<AiKnowledgeImportResponse>.Failure(published.Error);
                if (!published.IsSuccess) return await KnownFailureAsync(row, parameters, published.Error!, token).ConfigureAwait(false);
                row = (await store.FindAsync(parameters, token).ConfigureAwait(false))!;
            }
            if (row.StateKey == "pending_confirmation")
            {
                if (!store.IsTenant)
                {
                    try
                    {
                        var confirmed = await claims.ConfirmAsync(HostFileReferenceClaimIdempotencyKeys.AiKnowledgeVersion(row.VersionId), token).ConfigureAwait(false);
                        if (!confirmed.IsSuccess || confirmed.Value!.State != HostFileReferenceClaimStates.Active
                            || confirmed.Value.FileReference.FileId != row.FileId
                            || !Matches(row, confirmed.Value.FileReference.SizeBytes, confirmed.Value.FileReference.ContentHash))
                            return Result<AiKnowledgeImportResponse>.Success(Map((await store.FindAsync(parameters, token).ConfigureAwait(false))!));
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        return Result<AiKnowledgeImportResponse>.Success(Map(row));
                    }
                }
                _ = await store.StateAsync(parameters, "completed", null, token).ConfigureAwait(false);
            }
            return Result<AiKnowledgeImportResponse>.Success(Map((await store.FindAsync(parameters, token).ConfigureAwait(false))!));
        }
        finally
        {
            // 释放租约失败由五分钟期限收敛；不覆盖主要结果，也绝不在此处释放文件。
            using var leaseCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await store.ReleaseLeaseAsync(parameters, leaseCleanup.Token).ConfigureAwait(false); }
            catch (Exception) { }
        }
    }

    private async Task<Guid?> RecoverTenantFileAsync(AiKnowledgeImportRecord row, CancellationToken token)
    {
        if (!store.IsTenant) return null;
        var ready = await tenantFiles.ListReadyAsync("ai", row.VersionId, token).ConfigureAwait(false);
        foreach (var candidate in ready.Take(25))
        {
            var opened = await tenantFiles.OpenReadyContentAsync("ai", row.VersionId, candidate.FileId, token).ConfigureAwait(false);
            if (!opened.IsSuccess) continue;
            await using var stream = opened.Value!.Content;
            if (opened.Value.OriginalFileName != row.OriginalFileName || opened.Value.ContentType != row.ContentType) continue;
            var buffered = await AiKnowledgeUploadBuffer.ReadAsync(stream, row.ContentType, token).ConfigureAwait(false);
            if (!buffered.IsSuccess) continue;
            using var payload = buffered.Value!;
            if (Matches(row, payload.Size, payload.Hash)) return candidate.FileId;
        }
        return null;
    }

    private async Task<Result<AiKnowledgeImportResponse>> RejectUploadedAsync(AiKnowledgeImportRecord row,
        Dictionary<string, object?> parameters, Guid fileId, CancellationToken token)
    {
        if (fileId == Guid.Empty || !await store.SaveFileAsync(parameters, fileId, token).ConfigureAwait(false))
            return Conflict<AiKnowledgeImportResponse>();
        return await KnownFailureAsync((await store.FindAsync(parameters, token).ConfigureAwait(false))!, parameters,
            new Error("ai.knowledge.source_unavailable", "The uploaded file metadata did not match.", ErrorType.BusinessRule), token).ConfigureAwait(false);
    }

    private async Task<Result<AiKnowledgeImportResponse>> KnownFailureAsync(AiKnowledgeImportRecord row,
        Dictionary<string, object?> parameters, Error error, CancellationToken token)
    {
        // 仅明确的失败 Result 才补偿；状态必须先提交，释放异常留待所有者重试。
        if (await store.StateAsync(parameters, "cleanup_pending", error.Code, token).ConfigureAwait(false))
        {
            row = (await store.FindAsync(parameters, token).ConfigureAwait(false))!;
            _ = await CleanupAsync(row, parameters, token).ConfigureAwait(false);
        }
        return Result<AiKnowledgeImportResponse>.Failure(error);
    }

    private async Task<Result<AiKnowledgeImportResponse>> CleanupAsync(AiKnowledgeImportRecord row,
        Dictionary<string, object?> parameters, CancellationToken token)
    {
        try
        {
            if (store.IsTenant)
            {
                if (!await tenantCleanup.ReleaseOwnedAsync("ai", row.VersionId, token).ConfigureAwait(false))
                    return Result<AiKnowledgeImportResponse>.Success(Map(row));
            }
            else if (row.FileId is not null)
            {
                var released = await claims.ReleaseAsync(HostFileReferenceClaimIdempotencyKeys.AiKnowledgeVersion(row.VersionId), token).ConfigureAwait(false);
                if (!released.IsSuccess && released.Error!.Code != FilesErrorCodes.ClaimNotFound)
                    return Result<AiKnowledgeImportResponse>.Success(Map(row));
            }
            _ = await store.StateAsync(parameters, "failed", row.FailureCode, token).ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested) { }
        return Result<AiKnowledgeImportResponse>.Success(Map((await store.FindAsync(parameters, token).ConfigureAwait(false))!));
    }

    private static bool Matches(AiKnowledgeImportRecord row, long size, string? hash) =>
        row.SizeBytes == size && string.Equals(row.ContentHash, hash, StringComparison.OrdinalIgnoreCase);
}
