using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>续办只从真实来源步骤读取不可变代次；缺失来源时拒绝，不默认选择根代次。</summary>
internal static class WorkflowExecutionEpochScope
{
    public static async Task<bool> IsActiveAsync(IQueryExecutor query, Guid instanceId, string scope, Guid epochId, CancellationToken token)
    {
        if (instanceId == Guid.Empty || epochId == Guid.Empty || string.IsNullOrWhiteSpace(scope)) return false;
        var row = await query.QuerySingleOrDefaultAsync<WorkflowExecutionEpochRecord>(WorkflowExecutionEpochSql.Find,
            WorkflowSqlParameters.Create(("ExecutionEpochId", epochId), ("InstanceId", instanceId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        return row is not null && row.Id == epochId && row.InstanceId == instanceId && row.TenantScopeKey == scope && row.StatusKey == "active";
    }

    public static async Task<Guid?> ReadForStepAsync(IQueryExecutor query, Guid instanceId, string scope, Guid stepId, CancellationToken token)
    {
        if (instanceId == Guid.Empty || stepId == Guid.Empty || string.IsNullOrWhiteSpace(scope)) return null;
        var id = await query.QuerySingleOrDefaultAsync<Guid?>(WorkflowExecutionEpochSql.FindForStep,
            WorkflowSqlParameters.Create(("StepId", stepId), ("InstanceId", instanceId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        return id == Guid.Empty ? null : id;
    }
}
