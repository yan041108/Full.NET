using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>办理只按已完成提交收敛；个人完成不产生审批票，依次模式只激活下一冻结成员。</summary>
internal sealed class WorkflowTaskDecisionCoordinator(IQueryExecutor query, ICommandExecutor command,
    WorkflowHumanActivityActivationWriter activation, IOptions<DatabaseOptions> database)
{
    public async Task<Result<WorkflowTaskDecision>> ResolveAsync(WorkflowTaskContextRecord task, WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, WorkflowManagementScope scope, IReadOnlyDictionary<string, JsonElement> values,
        WorkflowTodoTimeoutPolicy? timeout, DateTimeOffset now, CancellationToken token)
    {
        if (task.TaskId != todo.StepId || todo.InstanceId != instance.Id || todo.NodeTypeKey != WorkflowHumanActivityKinds.Task ||
            new WorkflowTaskPolicy(task.CompletionModeKey).RequiredCompletions(task.MemberCount) != task.RequiredCompletionCount || task.RequiredCompletionCount < 1)
            return Invalid();
        var tally = await query.QuerySingleOrDefaultAsync<WorkflowTaskTallyRecord>(database.Value.Provider == DatabaseProvider.SqlServer
            ? WorkflowTaskSql.TallySqlServer : WorkflowTaskSql.TallyMySql,
            P(("TaskId", task.TaskId), ("InstanceId", instance.Id)), token).ConfigureAwait(false);
        if (tally is null || tally.CompletedCount < 1 || tally.PendingCount < 0 || tally.CancelledCount != 0 ||
            tally.CompletedCount + tally.PendingCount != task.MemberCount) return Invalid();
        var complete = tally.CompletedCount >= task.RequiredCompletionCount;
        Guid? next = null;
        if (complete)
        {
            if (await command.ExecuteAsync(WorkflowTaskSql.CompleteTask,
                P(("TaskId", task.TaskId), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("Revision", task.TaskRevision), ("Now", now)), token).ConfigureAwait(false) != 1) return Conflict();
            await command.ExecuteAsync(WorkflowTaskSql.CancelMemberTodos,
                P(("TaskId", task.TaskId), ("InstanceId", instance.Id), ("Now", now)), token).ConfigureAwait(false);
            await command.ExecuteAsync(WorkflowTaskSql.CancelMembers,
                P(("TaskId", task.TaskId), ("InstanceId", instance.Id), ("Now", now)), token).ConfigureAwait(false);
        }
        else if (task.CompletionModeKey == "sequential")
        {
            if (await query.QuerySingleOrDefaultAsync<int>(WorkflowTaskSql.CountActiveTodos,
                P(("TaskId", task.TaskId), ("InstanceId", instance.Id)), token).ConfigureAwait(false) != 0) return Conflict();
            var pending = await query.QuerySingleOrDefaultAsync<WorkflowPendingTaskMemberRecord>(database.Value.Provider == DatabaseProvider.SqlServer
                ? WorkflowTaskSql.FindNextSqlServer : WorkflowTaskSql.FindNextMySql,
                P(("TaskId", task.TaskId), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (pending is null) return Invalid();
            var assigned = await activation.ActivateTaskSequentialAsync(pending, todo, instance, scope.TenantScopeKey,
                timeout, values, now, token).ConfigureAwait(false);
            if (!assigned.IsSuccess) return Result<WorkflowTaskDecision>.Failure(assigned.Error!);
            next = assigned.Value;
        }
        return Result<WorkflowTaskDecision>.Success(new(complete, next, (int)tally.CompletedCount, (int)tally.PendingCount));
    }
    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowTaskDecision> Invalid() => Result<WorkflowTaskDecision>.Failure(new Error(
        WorkflowErrorCodes.SchemaInvalid, "The frozen task activity is inconsistent.", ErrorType.BusinessRule));
    private static Result<WorkflowTaskDecision> Conflict() => Result<WorkflowTaskDecision>.Failure(new Error(
        WorkflowErrorCodes.RevisionConflict, "The task activity changed.", ErrorType.Conflict));
}

/// <summary>提交结果只表示办理完成与后继待办，没有同意/驳回语义。</summary>
internal sealed record WorkflowTaskDecision(bool CompletesActivity, Guid? NextTodoId, int CompletedCount, int PendingCount);
