using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>固定成员约束不等于实时许可；读取、办理仍须另行检查当前授权状态。</summary>
internal static class WorkflowCollaborationOriginPolicy
{
    internal static async Task<IReadOnlyList<Guid>?> ReadMembersAsync(Guid instanceId, WorkflowManagementScope scope,
        IQueryExecutor query, CancellationToken token)
    {
        var origin = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationOriginRecord>(WorkflowCollaborationRelaySql.FindOrigin,
            WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (origin is null) return null;
        var admission = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission,
            WorkflowSqlParameters.Create(("Id", origin.CallId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (admission is null || !WorkflowCollaborationAdmissionProof.TryRead(admission, out var binding) ||
            binding!.TargetTenantScopeKey != scope.TenantScopeKey || admission.TargetInstanceId != origin.RootInstanceId) return [];
        var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
            WorkflowSqlParameters.Create(("Id", binding.GrantId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        return grant is not null && grant.PolicyHash == binding.PolicyHash &&
            WorkflowCollaborationGrantPolicy.TryReadAgreement(grant, out _, out var accepted) ? accepted!.AllowedUserIds : [];
    }
}
