using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>子额度由父根一次预留；每根只消费自身额度扣除后代预留后的余额。</summary>
internal static class WorkflowExecutionBudgetPolicy
{
    public static async Task<int> ReadRemainingAsync(WorkflowInstanceRecord instance, bool hasSubprocess,
        int usedCount, IQueryExecutor query, CancellationToken token)
    {
        if (!hasSubprocess && instance.BusinessType != WorkflowChildStartContext.BusinessType)
            return Math.Max(0, WorkflowActivityScheduler.InstanceActivityBudget - usedCount);
        var budget = await query.QuerySingleOrDefaultAsync<WorkflowExecutionBudgetRecord>(WorkflowSubprocessSql.FindBudget,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (budget is null) return instance.BusinessType == WorkflowChildStartContext.BusinessType ? -1 :
            Math.Max(0, WorkflowActivityScheduler.InstanceActivityBudget - usedCount);
        if (budget.Granted is < 2 or > WorkflowActivityScheduler.InstanceActivityBudget || budget.Reserved < 0 ||
            budget.Reserved > budget.Granted || budget.Revision < 1) return -1;
        return Math.Max(0, budget.Granted - budget.Reserved - usedCount);
    }
}
