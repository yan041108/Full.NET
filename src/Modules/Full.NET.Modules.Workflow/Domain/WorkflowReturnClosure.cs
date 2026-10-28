using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>调用者持有根锁且已完成来源Return；所有取消和失效与新轮次同事务提交。</summary>
internal static class WorkflowReturnClosure
{
    public static async Task ApplyAsync(ICommandExecutor commands, WorkflowInstanceRecord instance,
        WorkflowReturnScopeSelection selected, DateTimeOffset now, CancellationToken token)
    {
        var parameters = WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("Now", now),
            ("StepIds", selected.ActiveStepIds.ToArray()), ("InvalidatedStepIds", selected.InvalidatedStepIds.ToArray()),
            ("ForkIds", selected.ClosedForkIds.ToArray()), ("WorkIds", selected.ClosedWorkIds.ToArray()));
        // 先冻结投票计数再取消未投票；来源已经Returned，步骤取消只命中其余活动。
        foreach (var statement in new[] { WorkflowReturnClosureSql.CancelVotes, WorkflowReturnClosureSql.CancelBallots,
            WorkflowReturnClosureSql.CancelTodos, WorkflowReturnClosureSql.CancelSlots, WorkflowReturnClosureSql.CancelGroups,
            WorkflowReturnClosureSql.CancelTaskMembers, WorkflowReturnClosureSql.CancelTasks, WorkflowReturnClosureSql.CancelClaims,
            WorkflowReturnClosureSql.CancelClaimAttempts, WorkflowReturnClosureSql.CancelDelays, WorkflowReturnClosureSql.CancelSlas,
            WorkflowReturnClosureSql.CancelCountersignItems, WorkflowReturnClosureSql.CancelCountersignChains, WorkflowReturnClosureSql.CancelSteps })
            await commands.ExecuteAsync(statement, parameters, token).ConfigureAwait(false);
        if (selected.ClosedWorkIds.Count != 0) await commands.ExecuteAsync(WorkflowReturnClosureSql.CancelWork, parameters, token).ConfigureAwait(false);
        if (selected.ClosedForkIds.Count != 0)
        {
            await commands.ExecuteAsync(WorkflowReturnClosureSql.CloseEpochs, parameters, token).ConfigureAwait(false);
            await commands.ExecuteAsync(WorkflowReturnClosureSql.CancelForks, parameters, token).ConfigureAwait(false);
        }
        if (await commands.ExecuteAsync(WorkflowReturnClosureSql.InvalidateSteps, parameters, token).ConfigureAwait(false) != selected.InvalidatedStepIds.Count)
            throw new InvalidOperationException("workflow.return_scope.invalidation_conflict");
        await commands.ExecuteAsync(WorkflowReturnClosureSql.DeleteBindings, parameters, token).ConfigureAwait(false);
    }
}
