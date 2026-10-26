using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>采集只服务当前本人活动待办；以当前租户上传到 Files，实际办理仍须重新校验并绑定证据。</summary>
internal sealed class WorkflowSignatureUploadService(IQueryExecutor query, ICurrentTenant tenant,
    IHostFileUploadWriter hostFiles, ITenantResourceFileStore tenantFiles)
{
    public async Task<Result<WorkflowSignatureUploadResponse>> UploadAsync(Guid todoId, Guid actorId, long expectedRevision,
        string contentType, Stream content, long length, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        var todo = await query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            WorkflowSqlParameters.Create(("Id", todoId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (todo is null || todo.AssigneeUserId != actorId)
            return Failure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        if (todo.StatusKey != "active" || todo.Revision != expectedRevision)
            return Failure(WorkflowErrorCodes.TodoNotActive, ErrorType.Conflict);
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", todo.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance is null || instance.StatusKey != "active") return Failure(WorkflowErrorCodes.TodoNotActive, ErrorType.Conflict);
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || !WorkflowHumanActionPolicy.TryResolve(asset.CanonicalJson, todo.NodeKey, out var policy) ||
            !policy!.Actions.Any(action => action is "approve" or "reject" or "return" or "transfer" or "delegate" or "complete" or "vote" or "release"))
            return Failure(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        if (length is < 8 or > 1_048_576 || contentType is not ("image/png" or "image/jpeg"))
            return Failure(WorkflowErrorCodes.TodoSignatureInvalid, ErrorType.Validation);
        var bytes = new byte[(int)length + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await content.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        if (count != length || !WorkflowSignatureCoordinator.HasImageHeader(bytes, count, contentType))
            return Failure(WorkflowErrorCodes.TodoSignatureInvalid, ErrorType.Validation);
        using var validated = new MemoryStream(bytes, 0, count, writable: false);
        var name = contentType == "image/png" ? "signature.png" : "signature.jpg";
        if (scope.TenantId is not null)
        {
            var result = await tenantFiles.UploadAsync(WorkflowSignatureCoordinator.SignatureOwnerModuleKey, actorId,
                actorId, name, contentType, validated, count, token).ConfigureAwait(false);
            return result.IsSuccess ? Result<WorkflowSignatureUploadResponse>.Success(new(result.Value!.FileId,
                result.Value.SizeBytes, result.Value.ContentHash)) : Result<WorkflowSignatureUploadResponse>.Failure(result.Error!);
        }
        var uploaded = await hostFiles.UploadAsync(actorId, name, contentType, validated, count, token).ConfigureAwait(false);
        return uploaded.IsSuccess ? Result<WorkflowSignatureUploadResponse>.Success(new(uploaded.Value!.FileId,
            uploaded.Value.SizeBytes, uploaded.Value.ContentHash)) : Result<WorkflowSignatureUploadResponse>.Failure(uploaded.Error!);
    }

    private static Result<WorkflowSignatureUploadResponse> Failure(string code, ErrorType type) =>
        Result<WorkflowSignatureUploadResponse>.Failure(new Error(code, "Signature capture is invalid or unavailable for this todo.", type));
}
