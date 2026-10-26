using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>系统级取消只接受真实父项关联及已提交的关闭事实，不借用发起人身份伪造取消操作。</summary>
internal static class WorkflowSubprocessCancellationGuard
{
    public static async Task<bool> AllowsAsync(WorkflowInstanceRecord child, WorkflowChildStartContext context,
        WorkflowManagementScope scope, IQueryExecutor query, CancellationToken token)
    {
        if (context.ParentInstanceId == Guid.Empty || context.CallId == Guid.Empty || context.ItemId == Guid.Empty ||
            context.InputHash is not { Length: 64 } || !context.InputHash.All(char.IsAsciiHexDigitLower) ||
            child.BusinessType != WorkflowChildStartContext.BusinessType || child.BusinessId != context.ItemId.ToString("D") || child.TenantScopeKey != scope.TenantScopeKey) return false;
        var item = await query.QuerySingleOrDefaultAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.FindItem,
            WorkflowSqlParameters.Create(("Id", context.ItemId), ("CallId", context.CallId), ("InstanceId", context.ParentInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var call = await query.QuerySingleOrDefaultAsync<WorkflowSubprocessCallRecord>(WorkflowSubprocessSql.Find,
            WorkflowSqlParameters.Create(("Id", context.CallId), ("InstanceId", context.ParentInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (item is null || call is null || item.ChildInstanceId != child.Id || item.InputHash != context.InputHash || item.StarterUserId != child.StartedById ||
            call.ChildDefinitionVersionId != child.DefinitionVersionId || call.ChildFormVersionId != child.FormVersionId) return false;
        var parent = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", context.ParentInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var status = await query.QuerySingleOrDefaultAsync<string>(WorkflowSubprocessSql.StepStatus,
            WorkflowSqlParameters.Create(("Id", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (parent is null || status is null) return false;
        if (parent.StatusKey is "cancelled" or "rejected" || status != "active" || call.CancellationRequested) return true;
        var set = await query.QuerySingleOrDefaultAsync<WorkflowMultiInstanceSetRecord>(WorkflowMultiInstanceSql.Find,
            WorkflowSqlParameters.Create(("CallId", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (set?.StatusKey is not ("closing" or "faulted")) return false;
        var selected = await query.QueryAsync<Guid>(WorkflowMultiInstanceSql.Choices,
            WorkflowSqlParameters.Create(("CallId", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        return !selected.Contains(item.Id);
    }
}
