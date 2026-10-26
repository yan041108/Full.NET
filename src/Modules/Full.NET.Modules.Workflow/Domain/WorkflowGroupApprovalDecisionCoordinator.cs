using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>当前个人票已经写入后，先收敛所属组，再返回根组票数供统一办理事务决定步骤。</summary>
internal sealed class WorkflowGroupApprovalDecisionCoordinator(IQueryExecutor query, ICommandExecutor command,
    WorkflowHumanActivityActivationWriter activation, IOptions<DatabaseOptions> database)
{
    public async Task<Result<WorkflowGroupedApprovalDecision>> ResolveAsync(WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, WorkflowManagementScope scope, IReadOnlyDictionary<string, JsonElement> values,
        WorkflowTodoTimeoutPolicy? timeout, DateTimeOffset now, CancellationToken token)
    {
        var group = await query.QuerySingleOrDefaultAsync<WorkflowApprovalGroupContextRecord>(WorkflowGroupApprovalSql.FindForTodo,
            P(("TodoId", todo.Id), ("InstanceId", instance.Id), ("StepId", todo.StepId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (group is null || group.ApprovalGroupCount is < 2 or > 8 || group.RequiredApprovalCount < 1 || group.RequiredApprovalCount > group.SlotCount ||
            todo.RequiredApprovalCount is not { } rootRequired || rootRequired < 1 || rootRequired > group.ApprovalGroupCount)
            return Invalid();
        var tally = await query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(
            database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowGroupApprovalSql.TallySlotsSqlServer : WorkflowGroupApprovalSql.TallySlotsMySql,
            P(("GroupId", group.Id), ("StepId", todo.StepId), ("InstanceId", instance.Id)), token).ConfigureAwait(false);
        if (tally is null || tally.ApprovedCount + tally.RejectedCount + tally.PendingCount != group.SlotCount) return Invalid();
        var outcome = WorkflowApprovalDecision.Resolve(group.RequiredApprovalCount, (int)tally.ApprovedCount, (int)tally.PendingCount);
        Guid? nextTodo = null;
        if (outcome != WorkflowApprovalOutcome.Waiting)
        {
            if (await command.ExecuteAsync(WorkflowGroupApprovalSql.CompleteGroup,
                P(("GroupId", group.Id), ("StepId", todo.StepId), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey),
                    ("Revision", group.Revision), ("StatusKey", outcome == WorkflowApprovalOutcome.Approved ? "approved" : "rejected"), ("Now", now)), token).ConfigureAwait(false) != 1)
                return Conflict();
            await command.ExecuteAsync(WorkflowGroupApprovalSql.CancelGroupTodos,
                P(("GroupId", group.Id), ("StepId", todo.StepId), ("InstanceId", instance.Id), ("Now", now)), token).ConfigureAwait(false);
            await WorkflowHumanActivityLifecycle.CancelCountersignStepAsync(command, instance.Id, todo.StepId,
                scope.TenantScopeKey, group.Id, token).ConfigureAwait(false);
            await command.ExecuteAsync(WorkflowGroupApprovalSql.CancelGroupSlots,
                P(("GroupId", group.Id), ("StepId", todo.StepId), ("InstanceId", instance.Id), ("Now", now)), token).ConfigureAwait(false);
        }
        var root = await query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(
            database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowGroupApprovalSql.TallyGroupsSqlServer : WorkflowGroupApprovalSql.TallyGroupsMySql,
            P(("StepId", todo.StepId), ("InstanceId", instance.Id)), token).ConfigureAwait(false);
        if (root is null || root.ApprovedCount + root.RejectedCount + root.PendingCount != group.ApprovalGroupCount) return Invalid();
        if (outcome == WorkflowApprovalOutcome.Waiting && group.ModeKey == "sequential" &&
            WorkflowApprovalDecision.Resolve(rootRequired, (int)root.ApprovedCount, (int)root.PendingCount) == WorkflowApprovalOutcome.Waiting)
        {
            // 实例锁保护检查、创建和绑定；不能在UPDATE席位时用MySQL不支持的目标表自读子查询。
            if (await query.QuerySingleOrDefaultAsync<int>(WorkflowGroupApprovalSql.CountActiveTodos,
                P(("GroupId", group.Id), ("StepId", todo.StepId), ("InstanceId", instance.Id)), token).ConfigureAwait(false) != 0)
                return Conflict();
            var pending = await query.QuerySingleOrDefaultAsync<WorkflowPendingGroupApprovalSlotRecord>(
                database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowGroupApprovalSql.FindNextSqlServer : WorkflowGroupApprovalSql.FindNextMySql,
                P(("GroupId", group.Id), ("StepId", todo.StepId), ("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
            if (pending is null || pending.GroupId != group.Id) return Invalid();
            var activated = await activation.ActivateGroupedSequentialAsync(pending, todo, instance, scope.TenantScopeKey, timeout, values, now, token).ConfigureAwait(false);
            if (!activated.IsSuccess) return Result<WorkflowGroupedApprovalDecision>.Failure(activated.Error!);
            nextTodo = activated.Value;
        }
        return Result<WorkflowGroupedApprovalDecision>.Success(new(root, group.ApprovalGroupCount, nextTodo));
    }

    private static object P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<WorkflowGroupedApprovalDecision> Invalid() => Result<WorkflowGroupedApprovalDecision>.Failure(
        new Error(WorkflowErrorCodes.SchemaInvalid, "The frozen group approval facts are inconsistent.", ErrorType.BusinessRule));
    private static Result<WorkflowGroupedApprovalDecision> Conflict() => Result<WorkflowGroupedApprovalDecision>.Failure(
        new Error(WorkflowErrorCodes.RevisionConflict, "The group approval changed.", ErrorType.Conflict));
}

/// <summary>根步骤使用组票数；组内依次产生的新待办仍属于原组、原席位和原成员顺序。</summary>
internal sealed record WorkflowGroupedApprovalDecision(WorkflowApprovalTallyRecord Tally, int GroupCount, Guid? NextTodoId);
