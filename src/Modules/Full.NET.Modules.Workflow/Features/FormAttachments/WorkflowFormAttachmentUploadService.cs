using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>先授权固定字段和当前资源，再上传到本地Files；客户端不能选择其他租户或所有者。</summary>
internal sealed class WorkflowFormAttachmentUploadService(IQueryExecutor query, ICurrentTenant tenant, IClock clock,
    IDataTransactionState transaction, IActiveTenantContextResolver tenants, ITenantFeatureEntitlementPort entitlements,
    IHostFileUploadWriter hostFiles, ITenantResourceFileStore tenantFiles, WorkflowTodoManagementService todos)
{
    internal async Task<Result<WorkflowFormAttachmentUploadResponse>> ForStartAsync(Guid version, Guid actor, string fieldKey,
        string name, string contentType, Stream content, long length, CancellationToken token)
    {
        NoTransaction(); var scope = WorkflowManagementScope.Resolve(tenant);
        if (actor == Guid.Empty || version == Guid.Empty) return Invalid();
        if (await WorkflowTenantFeatureEntitlementGate.TryGetDenialAsync(scope, entitlements, token).ConfigureAwait(false) is { } error)
            return Result<WorkflowFormAttachmentUploadResponse>.Failure(error);
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", version), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || !WorkflowDefinitionLifecycleRules.AllowsNewInstance(asset.DefinitionStatusKey)) return Forbidden();
        if (!WorkflowDesignerRelease.AllowsExecution(await WorkflowDesignerRelease.ReadBindingsAsync(query, asset.FormVersionId, scope, token).ConfigureAwait(false))) return Forbidden();
        return await SaveAsync(JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema),
            fieldKey, actor, name, contentType, content, length, token).ConfigureAwait(false);
    }

    internal async Task<Result<WorkflowFormAttachmentUploadResponse>> ForTodoAsync(Guid todo, Guid actor, long expectedRevision,
        string fieldKey, string formKindKey, string name, string contentType, Stream content, long length, CancellationToken token)
    {
        NoTransaction(); var scope = WorkflowManagementScope.Resolve(tenant);
        if (actor == Guid.Empty || todo == Guid.Empty || expectedRevision < 1 || formKindKey is not ("main" or "vote")) return Invalid();
        if (await WorkflowTenantFeatureEntitlementGate.TryGetDenialAsync(scope, entitlements, token).ConfigureAwait(false) is { } error)
            return Result<WorkflowFormAttachmentUploadResponse>.Failure(error);
        var result = await todos.GetAsync(todo, actor, token).ConfigureAwait(false);
        if (!result.IsSuccess) return Result<WorkflowFormAttachmentUploadResponse>.Failure(result.Error!);
        var detail = result.Value!;
        if (detail.StatusKey != "active" || detail.Revision != expectedRevision) return Failure(ErrorType.Conflict);
        var root = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", detail.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (root?.StatusKey != "active" || !await WorkflowCollaborationRuntimeGate.AllowsAsync(root.Id, scope, actor, clock.UtcNow,
                query, token, tenants: tenants).ConfigureAwait(false)) return Forbidden();
        var policies = formKindKey == "vote" ? detail.VoteForm?.FieldPolicies : detail.FieldPolicies;
        if (policies is null || !policies.TryGetValue(fieldKey, out var policy) || policy is not ("editable" or "required") ||
            formKindKey == "vote" && detail.VoteForm!.ReadOnly) return Forbidden();
        var schema = JsonSerializer.Deserialize(formKindKey == "vote" ? detail.VoteForm!.FormSchema : detail.FormSchema,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        return await SaveAsync(schema, fieldKey, actor, name, contentType, content, length, token).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowFormAttachmentUploadResponse>> SaveAsync(WorkflowFormSchema? schema, string fieldKey, Guid actor,
        string name, string contentType, Stream content, long length, CancellationToken token)
    {
        var field = schema?.Sections.SelectMany(section => section.Fields).SingleOrDefault(field => field.FieldKey == fieldKey);
        name = Path.GetFileName((name ?? "").Replace('\\', '/')).Trim();
        if (field is null || !WorkflowFormAttachmentConstraints.TryRead(field, out _, out var maximum, out var extensions) ||
            length < 1 || length > maximum || name.Length is < 1 or > 255 || name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128 || contentType.Any(char.IsControl) ||
            !WorkflowFormAttachmentValueRules.MatchesAllowedExtension(name, extensions)) return Invalid();
        // 固定字段限制先于Files配额或存储副作用，实际读取也不能信任声明长度。
        using var validated = new MemoryStream(); var buffer = new byte[81_920]; int read;
        while ((read = await content.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (validated.Length > length - read) return Invalid();
            await validated.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        if (validated.Length != length) return Invalid(); validated.Position = 0;
        if (tenant.IsHost)
        {
            var saved = await hostFiles.UploadAsync(actor, name, contentType, validated, length, token).ConfigureAwait(false);
            return saved.IsSuccess ? Result<WorkflowFormAttachmentUploadResponse>.Success(new(saved.Value!.FileId, name, contentType,
                saved.Value.SizeBytes, saved.Value.ContentHash)) : Result<WorkflowFormAttachmentUploadResponse>.Failure(saved.Error!);
        }
        var resource = await tenantFiles.UploadAsync("workflow", actor, actor, name, contentType, validated, length, token).ConfigureAwait(false);
        return resource.IsSuccess ? Result<WorkflowFormAttachmentUploadResponse>.Success(new(resource.Value!.FileId, name, contentType,
            resource.Value.SizeBytes, resource.Value.ContentHash)) : Result<WorkflowFormAttachmentUploadResponse>.Failure(resource.Error!);
    }
    private void NoTransaction() { if (transaction.HasTransaction) throw new InvalidOperationException("File uploads cannot borrow a Workflow transaction."); }
    private static Result<WorkflowFormAttachmentUploadResponse> Invalid() => Failure(ErrorType.Validation);
    private static Result<WorkflowFormAttachmentUploadResponse> Forbidden() => Failure(ErrorType.Forbidden);
    private static Result<WorkflowFormAttachmentUploadResponse> Failure(ErrorType type) => Result<WorkflowFormAttachmentUploadResponse>.Failure(
        new(WorkflowErrorCodes.FormAttachmentInvalid, "The attachment upload is invalid or unavailable for this workflow field.", type));
}

/// <summary>当前上传者自己的文件身份和安全元数据，不表示远程能力签发。</summary>
internal sealed record WorkflowFormAttachmentUploadResponse(Guid FileId, string OriginalFileName, string ContentType, long SizeBytes, string? ContentHash);
