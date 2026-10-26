using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>一席已裁定后统一收敛组、原固定分母和后继活动，附属加签不能自行完成整个步骤。</summary>
internal sealed class WorkflowApprovalStepResolutionCoordinator(IQueryExecutor query, ICommandExecutor command,
    WorkflowGroupApprovalDecisionCoordinator groups, WorkflowHumanActivityActivationWriter activation,
    WorkflowRuntimeTransitionExecutor executor, IOptions<DatabaseOptions> database, WorkflowHumanActivityCompletionCoordinator callbacks)
{
    public async Task<Result<WorkflowApprovalStepResolution>> ResolveAsync(WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, WorkflowManagementScope scope, WorkflowRuntimePlan plan,
        IReadOnlyDictionary<string, JsonElement> values, WorkflowPreparedHumanActivities prepared,
        DateTimeOffset now, CancellationToken token, Guid? attributionUserId = null)
    {
        if (todo.RequiredApprovalCount is not { } required || todo.ApprovalSlotCount is not { } count ||
            required < 1 || count is < 1 or > 20 || todo.ApprovalModeKey != "group" && count < required)
            return Invalid();
        WorkflowApprovalTallyRecord? tally; Guid? nextTodo = null;
        plan.TryGetHumanTimeoutPolicy(todo.NodeKey, out var timeout);
        if (todo.ApprovalModeKey == "group")
        {
            var grouped = await groups.ResolveAsync(todo, instance, scope, values, timeout, now, token).ConfigureAwait(false);
            if (!grouped.IsSuccess) return Result<WorkflowApprovalStepResolution>.Failure(grouped.Error!);
            tally = grouped.Value!.Tally; count = grouped.Value.GroupCount; nextTodo = grouped.Value.NextTodoId;
        }
        else
            tally = await query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(
                database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowSql.FindApprovalTallyByStepSqlServer : WorkflowSql.FindApprovalTallyByStepMySql,
                P(("StepId", todo.StepId)), token).ConfigureAwait(false);
        if (tally is null || required > count || tally.ApprovedCount + tally.RejectedCount + tally.PendingCount != count)
            return Invalid();
        var outcome = WorkflowApprovalDecision.Resolve(required, (int)tally.ApprovedCount, (int)tally.PendingCount);
        if (outcome != WorkflowApprovalOutcome.Waiting)
        {
            var callback = await callbacks.ApplyAsync(instance, todo.StepId, todo.NodeKey, plan, values,
                outcome == WorkflowApprovalOutcome.Approved, attributionUserId, todo.ParallelJoinId, todo.ParallelBranchKey, now, token).ConfigureAwait(false);
            if (!callback.IsSuccess) return Result<WorkflowApprovalStepResolution>.Failure(callback.Error!);
            values = callback.Value!.Values;
        }
        WorkflowRuntimeTransition transition = default;
        if (outcome == WorkflowApprovalOutcome.Approved &&
            !plan.TryResolveAfterHuman(todo.NodeKey, values, todo.ParallelJoinNodeKey, out transition)) return Invalid();
        var changed = await command.ExecuteAsync(outcome == WorkflowApprovalOutcome.Waiting
                ? WorkflowSql.AdvanceApprovalStepWithRevision : WorkflowSql.CompleteStepWithRevision,
            P(("Id", todo.StepId), ("InstanceId", instance.Id), ("Revision", todo.StepRevision),
                ("StatusKey", outcome == WorkflowApprovalOutcome.Approved ? "completed" : "rejected"), ("CompletedAtUtc", now)), token).ConfigureAwait(false);
        if (changed != 1) return Conflict();
        if (outcome != WorkflowApprovalOutcome.Waiting)
        {
            await command.ExecuteAsync(WorkflowSql.CancelPendingApprovalTodosByStep,
                P(("StepId", todo.StepId), ("CompletedAtUtc", now)), token).ConfigureAwait(false);
            await WorkflowHumanActivityLifecycle.CancelCountersignStepAsync(command, instance.Id, todo.StepId,
                scope.TenantScopeKey, null, token).ConfigureAwait(false);
            await command.ExecuteAsync(WorkflowSql.CancelPendingApprovalSlotsByStep,
                P(("StepId", todo.StepId), ("DecidedAtUtc", now)), token).ConfigureAwait(false);
            if (todo.ApprovalModeKey == "group")
                await command.ExecuteAsync(WorkflowGroupApprovalSql.CancelActiveByStep,
                    P(("StepId", todo.StepId), ("InstanceId", instance.Id), ("Now", now)), token).ConfigureAwait(false);
        }
        if (outcome == WorkflowApprovalOutcome.Waiting && todo.ApprovalModeKey == "sequential")
        {
            var pending = await query.QuerySingleOrDefaultAsync<WorkflowPendingApprovalSlotRecord>(
                database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowSequentialApprovalSql.FindNextSqlServer : WorkflowSequentialApprovalSql.FindNextMySql,
                P(("InstanceId", instance.Id), ("StepId", todo.StepId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (pending is null) return Invalid();
            var activated = await activation.ActivateSequentialAsync(pending, todo, instance, scope.TenantScopeKey,
                timeout, values, now, token).ConfigureAwait(false);
            if (!activated.IsSuccess) return Result<WorkflowApprovalStepResolution>.Failure(activated.Error!);
            nextTodo = activated.Value;
        }
        var status = outcome == WorkflowApprovalOutcome.Rejected ? "rejected" : "active";
        if (outcome == WorkflowApprovalOutcome.Approved)
        {
            var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
                P(("InstanceId", instance.Id)), token).ConfigureAwait(false);
            var executed = await executor.ExecuteAsync(instance, scope, plan, transition, values, sequence, now,
                instance.StartedById, todo.ParallelJoinId, todo.ParallelBranchKey, token, preparedAssignees: prepared).ConfigureAwait(false);
            if (!executed.IsSuccess) return Result<WorkflowApprovalStepResolution>.Failure(executed.Error!);
            nextTodo = executed.Value!.FirstTodoId; status = executed.Value.InstanceStatusKey;
        }
        return Result<WorkflowApprovalStepResolution>.Success(new(tally, outcome, status, nextTodo));
    }
    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowApprovalStepResolution> Invalid() => Result<WorkflowApprovalStepResolution>.Failure(
        new(WorkflowErrorCodes.SchemaInvalid, "The frozen approval facts are inconsistent.", ErrorType.BusinessRule));
    private static Result<WorkflowApprovalStepResolution> Conflict() => Result<WorkflowApprovalStepResolution>.Failure(
        new(WorkflowErrorCodes.RevisionConflict, "The approval activity changed.", ErrorType.Conflict));
}

/// <summary>步骤收敛不提交实例或动作回执；调用方在同一事务内记录真实执行人与固定响应。</summary>
internal sealed record WorkflowApprovalStepResolution(WorkflowApprovalTallyRecord Tally, WorkflowApprovalOutcome Outcome,
    string InstanceStatusKey, Guid? NextTodoId);
