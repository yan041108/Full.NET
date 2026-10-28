using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

internal sealed partial class WorkflowRuntimeTransitionExecutor
{
    private async Task<Result<WorkflowTransitionExecutionResult>> ExecuteHandlingAsync(WorkflowInstanceRecord instance,
        WorkflowManagementScope scope, WorkflowRuntimePlan plan, string nodeKey, WorkflowHumanActivityAssignees assignees,
        IReadOnlyDictionary<string, JsonElement> values, long sequence, DateTimeOffset now, Guid initiator,
        Guid? joinId, string? branchKey, CancellationToken token, WorkflowDispatchBudget budget,
        WorkflowPreparedHumanActivities? prepared, WorkflowActivationTrace trace)
    {
        var kind = plan.FindNodeTypeKey(nodeKey); var disposition = assignees.Handling!;
        if (!WorkflowHumanActivityKinds.IsFormActivity(kind) || disposition.ActionKey is not ("suspend" or "skip" or "auto_pass" or "auto_reject"))
            return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
        var stepId = await humanActivationWriter.WriteHandlingAsync(instance, scope, budget.ExecutionEpochId, nodeKey, kind!, assignees,
            sequence, now, joinId, branchKey, token, trace: trace).ConfigureAwait(false);
        if (disposition.ActionKey != "suspend")
        {
            var callback = await callbacks.ApplyAsync(instance, stepId, nodeKey, plan, values,
                disposition.ActionKey != "auto_reject", null, joinId, branchKey, now, token).ConfigureAwait(false);
            if (!callback.IsSuccess) return Result<WorkflowTransitionExecutionResult>.Failure(callback.Error!);
            values = callback.Value!.Values;
        }
        if (disposition.ActionKey == "suspend")
        {
            var revision = await branchStates.ReadRevisionAsync(instance, joinId, branchKey, token).ConfigureAwait(false);
            if (revision is null) return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            // 续办保留同一节点和输入，管理员恢复后重新解析真实资格；此次系统裁定已占用独立步骤序号。
            await activityScheduler.EnqueueAsync(instance, budget.ExecutionEpochId, nodeKey, values, revision.Value, sequence + 1, joinId, branchKey, now, token, sourceStepId: trace.LastStepId,causeKindKey:trace.KindKey).ConfigureAwait(false);
            return Result<WorkflowTransitionExecutionResult>.Success(new(sequence + 1, null, false, true));
        }
        if (disposition.ActionKey == "auto_reject")
        {
            await humanActivationWriter.CancelAfterHandlingAsync(instance, scope, now, budget.CurrentWorkId, token).ConfigureAwait(false);
            await parallelJoinCoordinator.CancelWaitingJoinsAsync(instance.Id, now, token).ConfigureAwait(false);
            return Result<WorkflowTransitionExecutionResult>.Success(new(sequence + 1, null, false, RejectsInstance: true));
        }
        var join = joinId is { } id
            ? await queryExecutor.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById,
                WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instance.Id)), token).ConfigureAwait(false) : null;
        if (joinId is not null && join?.StatusKey != "waiting" || !plan.TryResolveAfterHuman(nodeKey, values, join?.JoinNodeKey, out var next))
            return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
        return await ExecuteAsync(instance, scope, budget.ExecutionEpochId, plan, next, values, sequence + 1, now, initiator,
            joinId, branchKey, token, budget, prepared, trace: trace).ConfigureAwait(false);
    }
}
