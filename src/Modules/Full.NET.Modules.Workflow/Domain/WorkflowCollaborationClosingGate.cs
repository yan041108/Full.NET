using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>只证明本租户终态资源属于已关闭意图；上层仍须精确对账权限，不能据此重试或推进业务。</summary>
internal static class WorkflowCollaborationClosingGate
{
    internal static async Task<bool> AllowsAsync(Guid instanceId, WorkflowManagementScope scope, IQueryExecutor query,
        CancellationToken token, ICommandExecutor? commands = null)
    {
        var origin = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationOriginRecord>(WorkflowCollaborationRelaySql.FindOrigin,
            WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (origin is null) return false;
        var admission = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission,
            WorkflowSqlParameters.Create(("Id", origin.CallId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (admission is null || admission.StatusKey is not ("cancel_requested" or "cancelled") || admission.TargetInstanceId != origin.RootInstanceId ||
            !WorkflowCollaborationAdmissionProof.TryRead(admission, out var proof) || proof!.TargetTenantScopeKey != scope.TenantScopeKey) return false;
        var instance = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var root = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", origin.RootInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance?.StatusKey is not ("completed" or "rejected" or "cancelled") || root?.StatusKey is not ("completed" or "rejected" or "cancelled") ||
            root.BusinessType != WorkflowCollaborationStartContext.BusinessType || root.BusinessId != admission.Id.ToString("D") || root.DefinitionVersionId != proof.TargetDefinitionVersionId) return false;
        if (commands is null) return true;
        return await commands.ExecuteAsync(WorkflowCollaborationControlSql.LockTerminalReconciliation,
            WorkflowSqlParameters.Create(("Id", admission.Id), ("TenantScopeKey", scope.TenantScopeKey),
                ("BindingHash", admission.BindingHash), ("TargetInstanceId", origin.RootInstanceId)), token).ConfigureAwait(false) == 1;
    }
}
