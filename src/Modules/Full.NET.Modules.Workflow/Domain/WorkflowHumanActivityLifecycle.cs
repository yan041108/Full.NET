using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>实例终态在同一本模块锁事务收敛加签和分组事实，不能仅关闭可见待办。</summary>
internal static class WorkflowHumanActivityLifecycle
{
    /// <summary>拒绝终态关闭全部并行前沿与附属授权，调用方已经持有可信实例锁并完成实例CAS。</summary>
    public static async Task RejectAsync(ICommandExecutor commands, Guid instanceId, string tenantScopeKey, DateTimeOffset now, CancellationToken token)
    {
        await CancelAsync(commands, instanceId, tenantScopeKey, now, token).ConfigureAwait(false);
        var parameters = WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey),
            ("Now", now), ("CompletedAtUtc", now), ("ResultActionKey", "cancelled"), ("StatusKey", "cancelled"));
        await commands.ExecuteAsync(WorkflowActivitySql.CancelApprovalSlots, parameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.CancelAllActiveTodosByInstance, parameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.CancelAllActiveStepsByInstance, parameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.CancelWaitingParallelJoinsByInstance, parameters, token).ConfigureAwait(false);
    }
    /// <summary>已完成组不能恢复附属的前加签原席；全部旧项先关闭，再关闭链。</summary>
    public static async Task CancelCountersignStepAsync(ICommandExecutor commands, Guid instanceId, Guid stepId,
        string tenantScopeKey, Guid? groupId, CancellationToken token)
    {
        var parameters = WorkflowSqlParameters.Create(("InstanceId", instanceId), ("StepId", stepId),
            ("TenantScopeKey", tenantScopeKey), ("GroupId", groupId));
        await commands.ExecuteAsync(WorkflowCountersignClosureSql.CancelItems, parameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowCountersignClosureSql.CancelChains, parameters, token).ConfigureAwait(false);
    }
    public static async Task CancelAsync(ICommandExecutor commands, Guid instanceId, string tenantScopeKey, DateTimeOffset now, CancellationToken token)
    {
        var parameters = WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey));
        await commands.ExecuteAsync(WorkflowSql.CancelCountersignItemsByInstance, parameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.CancelCountersignChainsByInstance, parameters, token).ConfigureAwait(false);
        var taskParameters = WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey), ("Now", now));
        await commands.ExecuteAsync(WorkflowSlaSql.CancelInstance, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowDelaySql.Cancel, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowConnectorSql.CancelInstance, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowAsyncBarrierSql.CancelInstance, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowTaskSql.CancelMembersByInstance, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowTaskSql.CancelByInstance, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowVoteSql.CancelRoundsByInstance, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowVoteSql.CancelBallotsByInstance, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowClaimSql.CancelAttempts, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowClaimSql.CancelPools, taskParameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowGroupApprovalSql.CancelActiveByInstance,
            WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey), ("Now", now)), token).ConfigureAwait(false);
    }
}
