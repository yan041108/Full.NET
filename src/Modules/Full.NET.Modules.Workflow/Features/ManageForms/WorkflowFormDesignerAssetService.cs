using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Contracts;
namespace Full.NET.Modules.Workflow.Features.ManageForms;

internal sealed partial class WorkflowFormManagementService
{
    /// <summary>只向表单设计授权接口返回完整资产，待办运行不调用此路径。</summary>
    public async Task<Result<WorkflowDesignerAssetResponse>> GetDesignerAssetAsync(Guid id, CancellationToken cancellationToken)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var form = await FindAsync(id, scope, cancellationToken).ConfigureAwait(false);
        if (form is null) return NotFound<WorkflowDesignerAssetResponse>();
        var asset = await FindDesignerAssetAsync(id, scope, cancellationToken).ConfigureAwait(false);
        return asset is null ? NotFound<WorkflowDesignerAssetResponse>() : Result<WorkflowDesignerAssetResponse>.Success(MapDesigner(asset));
    }

    /// <summary>无损保存有界原版 JSON，修订竞争时整笔回滚，保留首次持久化内容。</summary>
    public async Task<Result<WorkflowDesignerAssetResponse>> SaveDesignerAssetAsync(Guid id, SaveWorkflowDesignerAssetRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision < 1 || request.SourceCommit != WorkflowDesignerAsset.SourceCommit || request.FormatVersion != WorkflowDesignerAsset.FormatVersion || !WorkflowDesignerAsset.IsValid(request.DesignerJson)) return Invalid<WorkflowDesignerAssetResponse>();
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        if (await TryDenyTenantMutationAsync<WorkflowDesignerAssetResponse>(scope, cancellationToken).ConfigureAwait(false) is { } denied) return denied;
        return await transaction.ExecuteResultAsync(async token =>
        {
            var form = await FindAsync(id, scope, token).ConfigureAwait(false);
            if (form is null) return NotFound<WorkflowDesignerAssetResponse>();
            if (!WorkflowFormLifecycleRules.AllowsDraftMutation(form.StatusKey)) return Failure<WorkflowDesignerAssetResponse>(WorkflowErrorCodes.FormArchived, ErrorType.Conflict);
            var changed = await commandExecutor.ExecuteAsync(WorkflowSql.UpdateFormDraft, Parameters(("Id", id), ("TenantScopeKey", scope.TenantScopeKey),
                ("DraftSchemaJson", form.DraftSchemaJson), ("UpdatedAtUtc", clock.UtcNow), ("ExpectedRevision", request.ExpectedRevision)), token).ConfigureAwait(false);
            if (changed != 1) return RevisionConflict<WorkflowDesignerAssetResponse>();
            var previous = await FindDesignerAssetAsync(id, scope, token).ConfigureAwait(false);
            var artifact = WorkflowJsonCanonicalizer.Compile(request.DesignerJson);
            await commandExecutor.ExecuteAsync(previous is null ? WorkflowDesignerAssetSql.Insert : WorkflowDesignerAssetSql.Update,
                Parameters(("FormId", id), ("TenantScopeKey", scope.TenantScopeKey), ("SourceCommit", request.SourceCommit), ("FormatVersion", request.FormatVersion),
                ("DesignerJson", artifact.CanonicalJson), ("ContentHash", artifact.ContentHash)), token).ConfigureAwait(false);
            return Result<WorkflowDesignerAssetResponse>.Success(new(id, request.ExpectedRevision + 1, request.SourceCommit, request.FormatVersion, request.DesignerJson.Clone(), artifact.ContentHash));
        }, cancellationToken).ConfigureAwait(false);
    }
    private Task<WorkflowDesignerAssetRecord?> FindDesignerAssetAsync(Guid id, WorkflowManagementScope scope, CancellationToken token) =>
        queryExecutor.QuerySingleOrDefaultAsync<WorkflowDesignerAssetRecord>(WorkflowDesignerAssetSql.Find, Parameters(("FormId", id), ("TenantScopeKey", scope.TenantScopeKey)), token);
    private static WorkflowDesignerAssetResponse MapDesigner(WorkflowDesignerAssetRecord record)
    {
        using var document = JsonDocument.Parse(record.DesignerJson);
        return new(record.FormId, record.DraftRevision, record.SourceCommit, record.FormatVersion, document.RootElement.Clone(), record.ContentHash);
    }
}
