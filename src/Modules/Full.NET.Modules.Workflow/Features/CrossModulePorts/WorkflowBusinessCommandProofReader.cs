using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using System.Text.Json;

namespace Full.NET.Modules.Workflow.Features.CrossModulePorts;

/// <summary>向真实消费者提供最小权威调用证明，租户仅从当前可信基础设施上下文取得。</summary>
internal sealed class WorkflowBusinessCommandProofReader(IQueryExecutor query, ICurrentTenant tenant,
    WorkflowBusinessActionCatalog catalog) : IWorkflowBusinessCommandProofReader
{
    public async Task<WorkflowBusinessCommandProof?> FindAsync(Guid callId, Guid instanceId, string inputHash, CancellationToken token)
    {
        if (callId == Guid.Empty || instanceId == Guid.Empty || inputHash is not { Length: 64 }) return null;
        var scope = WorkflowManagementScope.Resolve(tenant);
        var row = await query.QuerySingleOrDefaultAsync<WorkflowBusinessCommandRecord>(WorkflowBusinessCommandSql.Find,
            WorkflowSqlParameters.Create(("Id", callId), ("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (row is null || row.InputHash != inputHash) return null;
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance is null || WorkflowBusinessCommandValidation.Read(row, instance, catalog) is not { } binding) return null;
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null) return null;
        WorkflowDefinitionDraft? draft;
        try { draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft); }
        catch (JsonException) { return null; }
        if (draft is null || !WorkflowBusinessCommandValidation.MatchesDefinition(row, draft)) return null;
        return new(binding, inputHash, instance.StatusKey, row.StatusKey, row.CompensationRequested, row.Generation);
    }
}
