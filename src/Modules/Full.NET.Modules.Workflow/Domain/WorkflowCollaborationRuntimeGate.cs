using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>共享授权与准入是实时权威；成员快照只收紧允许范围，不能提供旧缓存许可。</summary>
internal static class WorkflowCollaborationRuntimeGate
{
    public static Task<bool> AllowsAsync(Guid instanceId, WorkflowManagementScope scope, Guid? actor,
        DateTimeOffset now, IQueryExecutor query, CancellationToken token, ICommandExecutor? commands = null,
        IActiveTenantContextResolver? tenants = null, Guid? selectedUser = null) =>
        AllowsCoreAsync(instanceId, scope, actor, now, query, token, commands, tenants, selectedUser, false);

    /// <summary>成功接纳后只保留当前授权下的真实目标历史；取消、撤销和新业务推进不使用此例外。</summary>
    internal static Task<bool> AllowsReadAsync(Guid instanceId, WorkflowManagementScope scope, Guid? actor,
        DateTimeOffset now, IQueryExecutor query, CancellationToken token, IActiveTenantContextResolver? tenants = null) =>
        AllowsCoreAsync(instanceId, scope, actor, now, query, token, null, tenants, null, true);

    private static async Task<bool> AllowsCoreAsync(Guid instanceId, WorkflowManagementScope scope, Guid? actor,
        DateTimeOffset now, IQueryExecutor query, CancellationToken token, ICommandExecutor? commands,
        IActiveTenantContextResolver? tenants, Guid? selectedUser, bool completedRead)
    {
        var origin = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationOriginRecord>(WorkflowCollaborationRelaySql.FindOrigin,
            WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (origin is null) return true;
        var admission = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission,
            WorkflowSqlParameters.Create(("Id", origin.CallId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (admission is null || !(admission.StatusKey is "created" or "active" || completedRead && admission.StatusKey == "completed") || admission.TargetInstanceId != origin.RootInstanceId ||
            !WorkflowCollaborationAdmissionProof.TryRead(admission, out var proof) || proof!.TargetTenantScopeKey != scope.TenantScopeKey ||
            proof.TargetTenantId != scope.TenantId || origin.Depth < proof.Depth || origin.Depth > WorkflowSubprocessPolicy.MaximumDepth) return false;
        var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
            WorkflowSqlParameters.Create(("Id", proof.GrantId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (grant is null || grant.PolicyHash != proof.PolicyHash || grant.SourceTenantScopeKey != proof.SourceTenantScopeKey ||
            grant.TargetTenantScopeKey != proof.TargetTenantScopeKey || grant.SourceTenantId != proof.SourceTenantId || grant.TargetTenantId != proof.TargetTenantId || !WorkflowCollaborationGrantPolicy.TryAuthorize(grant,
                scope.TenantScopeKey, true, proof.GrantRevision, now, out _, out var accepted) ||
            actor is { } user && !accepted!.AllowedUserIds.Contains(user) || selectedUser is { } selected && !accepted!.AllowedUserIds.Contains(selected)) return false;
        if (tenants is not null &&
            (proof.SourceTenantId is { } source && await tenants.ResolveActiveByIdAsync(source, token).ConfigureAwait(false) is null ||
             proof.TargetTenantId is { } target && await tenants.ResolveActiveByIdAsync(target, token).ConfigureAwait(false) is null)) return false;
        if (admission.StatusKey == "completed")
        {
            var root = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
                WorkflowSqlParameters.Create(("Id", origin.RootInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (root?.StatusKey != "completed" || root.DefinitionVersionId != proof.TargetDefinitionVersionId || root.TenantScopeKey != scope.TenantScopeKey) return false;
        }
        if (commands is null) return true;
        // 调用方持有目标根本地事务时，控制面锁把撤销、取消与本次写入线性化；这里不调用外模块Port。
        var parameters = WorkflowSqlParameters.Create(("Id", proof.CallId), ("GrantId", proof.GrantId), ("TenantScopeKey", scope.TenantScopeKey),
            ("SourceTenantScopeKey", proof.SourceTenantScopeKey), ("GrantRevision", proof.GrantRevision), ("PolicyHash", proof.PolicyHash),
            ("BindingHash", admission.BindingHash), ("RootInstanceId", origin.RootInstanceId), ("Now", now));
        return await commands.ExecuteAsync(WorkflowCollaborationRelaySql.LockGrant, parameters, token).ConfigureAwait(false) == 1 &&
            await commands.ExecuteAsync(WorkflowCollaborationRelaySql.LockActiveAdmission, parameters, token).ConfigureAwait(false) == 1;
    }
}
