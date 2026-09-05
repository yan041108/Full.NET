using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在 Workflow 本地事务中执行审批迁移，包括并行分叉、汇合与下一审批激活。</summary>
/// <param name="automaticTransitionWriter">自动节点写入器。</param>
/// <param name="approvalActivationWriter">审批激活写入器。</param>
/// <param name="parallelJoinCoordinator">并行汇合协调器。</param>
/// <param name="approvalAssigneeCoordinator">办理人解析协调器。</param>
/// <param name="queryExecutor">显式 SQL 查询执行器。</param>
internal sealed class WorkflowApprovalTransitionExecutor(
    WorkflowAutomaticTransitionWriter automaticTransitionWriter,
    WorkflowApprovalActivationWriter approvalActivationWriter,
    WorkflowParallelJoinCoordinator parallelJoinCoordinator,
    WorkflowApprovalAssigneeCoordinator approvalAssigneeCoordinator,
    IQueryExecutor queryExecutor)
{
    /// <summary>执行一次闭合审批迁移并返回首个新待办标识（若存在）。</summary>
    /// <param name="instance">当前实例。</param>
    /// <param name="scope">可信作用域。</param>
    /// <param name="runtimePlan">发布定义运行计划。</param>
    /// <param name="transition">待执行的闭合迁移。</param>
    /// <param name="values">实例当前表单值。</param>
    /// <param name="executionSequence">首个自动步骤可用执行序号。</param>
    /// <param name="now">业务发生时间。</param>
    /// <param name="startedById">实例发起人，用于办理人解析。</param>
    /// <param name="activeParallelJoinId">当前分支所属汇合状态；汇合到达时必填。</param>
    /// <param name="activeParallelBranchKey">当前分支键；并行分支审批时必填。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果或稳定错误。</returns>
    public async Task<Result<WorkflowTransitionExecutionResult>> ExecuteAsync(
        WorkflowInstanceRecord instance,
        WorkflowManagementScope scope,
        WorkflowRuntimePlan runtimePlan,
        WorkflowApprovalTransition transition,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values,
        long executionSequence,
        DateTimeOffset now,
        Guid startedById,
        Guid? activeParallelJoinId = null,
        string? activeParallelBranchKey = null,
        CancellationToken cancellationToken = default)
    {
        if (transition.ParallelFork is { } fork)
        {
            return await ExecuteParallelForkAsync(
                instance, scope, fork, executionSequence, now, startedById, cancellationToken)
                .ConfigureAwait(false);
        }

        var sequence = await automaticTransitionWriter.WriteAsync(
            instance.Id,
            scope.TenantScopeKey,
            transition.AutomaticNodes,
            executionSequence,
            now,
            cancellationToken).ConfigureAwait(false);

        if (transition.WaitsAtJoin && transition.JoinArrival is { } joinArrival &&
            joinArrival.BranchKey is { Length: > 0 } branchKey)
        {
            return await ExecuteJoinArrivalAsync(
                instance,
                scope,
                runtimePlan,
                values,
                joinArrival,
                branchKey,
                activeParallelJoinId,
                sequence,
                now,
                startedById,
                cancellationToken).ConfigureAwait(false);
        }

        if (transition.CompletesInstance)
        {
            return Result<WorkflowTransitionExecutionResult>.Success(
                new WorkflowTransitionExecutionResult(sequence, null, true));
        }

        if (transition.NextApprovalNodeKey is not { } nextApprovalNodeKey)
        {
            return Result<WorkflowTransitionExecutionResult>.Failure(
                WorkflowTransitionExecutionErrors.InvalidTransition());
        }

        var nextTodoId = await ActivateApprovalAsync(
            instance,
            scope,
            transition,
            nextApprovalNodeKey,
            activeParallelJoinId,
            activeParallelBranchKey,
            sequence,
            now,
            startedById,
            cancellationToken).ConfigureAwait(false);
        if (!nextTodoId.IsSuccess)
        {
            return Result<WorkflowTransitionExecutionResult>.Failure(nextTodoId.Error!);
        }

        return Result<WorkflowTransitionExecutionResult>.Success(
            new WorkflowTransitionExecutionResult(sequence, nextTodoId.Value, false));
    }

    /// <summary>执行并行分叉并同时激活各分支首个等待点。</summary>
    private async Task<Result<WorkflowTransitionExecutionResult>> ExecuteParallelForkAsync(
        WorkflowInstanceRecord instance,
        WorkflowManagementScope scope,
        WorkflowParallelForkPlan fork,
        long executionSequence,
        DateTimeOffset now,
        Guid startedById,
        CancellationToken cancellationToken)
    {
        var joinId = await parallelJoinCoordinator.CreateJoinAsync(
            instance.Id,
            fork.ForkNodeKey,
            fork.JoinNodeKey,
            fork.Branches.Count,
            now,
            cancellationToken).ConfigureAwait(false);
        var sequence = await automaticTransitionWriter.WriteAsync(
            instance.Id,
            scope.TenantScopeKey,
            [new WorkflowAutomaticRuntimeNode(
                fork.ForkNodeKey,
                "gateway.parallel",
                [],
                fork.JoinNodeKey)],
            executionSequence,
            now,
            cancellationToken).ConfigureAwait(false);
        Guid? firstTodoId = null;
        foreach (var branch in fork.Branches)
        {
            sequence = await automaticTransitionWriter.WriteAsync(
                instance.Id,
                scope.TenantScopeKey,
                branch.AutomaticNodes,
                sequence,
                now,
                cancellationToken).ConfigureAwait(false);
            if (branch.NextApprovalNodeKey is not { } approvalNodeKey)
            {
                continue;
            }

            var activation = await ActivateApprovalAsync(
                instance,
                scope,
                new WorkflowApprovalTransition(
                    approvalNodeKey,
                    branch.CompletesInstance,
                    [],
                    branch.TimeoutPolicy,
                    branch.ApprovalPolicy,
                    branch.AssigneePolicy),
                approvalNodeKey,
                joinId,
                branch.BranchKey,
                sequence,
                now,
                startedById,
                cancellationToken).ConfigureAwait(false);
            if (!activation.IsSuccess)
            {
                return Result<WorkflowTransitionExecutionResult>.Failure(activation.Error!);
            }

            firstTodoId ??= activation.Value;
        }

        return Result<WorkflowTransitionExecutionResult>.Success(
            new WorkflowTransitionExecutionResult(sequence, firstTodoId, false));
    }

    /// <summary>记录分支到达汇合点，并在全部分支到达后继续主路径。</summary>
    private async Task<Result<WorkflowTransitionExecutionResult>> ExecuteJoinArrivalAsync(
        WorkflowInstanceRecord instance,
        WorkflowManagementScope scope,
        WorkflowRuntimePlan runtimePlan,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values,
        WorkflowJoinArrivalPlan joinArrival,
        string branchKey,
        Guid? activeParallelJoinId,
        long executionSequence,
        DateTimeOffset now,
        Guid startedById,
        CancellationToken cancellationToken)
    {
        var parallelJoinId = activeParallelJoinId ?? await queryExecutor.QuerySingleOrDefaultAsync<Guid?>(
            WorkflowSql.FindWaitingParallelJoinByInstanceAndJoinNode,
            WorkflowSqlParameters.Create(
                ("InstanceId", instance.Id),
                ("JoinNodeKey", joinArrival.JoinNodeKey)),
            cancellationToken).ConfigureAwait(false);
        if (parallelJoinId is null || parallelJoinId == Guid.Empty)
        {
            return Result<WorkflowTransitionExecutionResult>.Failure(
                WorkflowTransitionExecutionErrors.InvalidTransition());
        }

        var join = await parallelJoinCoordinator.TryRecordArrivalAsync(
            instance.Id,
            parallelJoinId.Value,
            branchKey,
            now,
            cancellationToken).ConfigureAwait(false);

        var sequence = await automaticTransitionWriter.WriteAsync(
            instance.Id,
            scope.TenantScopeKey,
            [
                ..joinArrival.TrailingAutomaticNodes,
                new WorkflowAutomaticRuntimeNode(
                    joinArrival.JoinNodeKey,
                    "gateway.parallel",
                    [],
                    "joined"),
            ],
            executionSequence,
            now,
            cancellationToken).ConfigureAwait(false);

        if (!join.IsJoinComplete)
        {
            return Result<WorkflowTransitionExecutionResult>.Success(
                new WorkflowTransitionExecutionResult(sequence, null, false));
        }

        if (!runtimePlan.TryResolveAfterJoin(joinArrival.JoinNodeKey, values, out var afterJoin))
        {
            return Result<WorkflowTransitionExecutionResult>.Failure(
                WorkflowTransitionExecutionErrors.InvalidTransition());
        }

        return await ExecuteAsync(
            instance,
            scope,
            runtimePlan,
            afterJoin,
            values,
            sequence,
            now,
            startedById,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>激活下一人工审批并返回首个待办标识。</summary>
    private async Task<Result<Guid>> ActivateApprovalAsync(
        WorkflowInstanceRecord instance,
        WorkflowManagementScope scope,
        WorkflowApprovalTransition transition,
        string nodeKey,
        Guid? parallelJoinId,
        string? parallelBranchKey,
        long executionSequence,
        DateTimeOffset now,
        Guid startedById,
        CancellationToken cancellationToken)
    {
        var assignees = await approvalAssigneeCoordinator.ResolveAsync(
                transition.AssigneePolicy,
                transition.ApprovalPolicy,
                scope,
                startedById,
                cancellationToken)
            .ConfigureAwait(false);
        if (!assignees.IsSuccess)
        {
            return Result<Guid>.Failure(assignees.Error!);
        }

        var activation = parallelJoinId is { } joinId && parallelBranchKey is not null
            ? await approvalActivationWriter.WriteParallelAsync(
                instance.Id,
                scope.TenantScopeKey,
                nodeKey,
                assignees.Value!.ApprovalPolicy,
                assignees.Value.FallbackAssigneeUserId,
                executionSequence,
                now,
                transition.TimeoutPolicy,
                instance.BusinessType,
                instance.BusinessId,
                joinId,
                parallelBranchKey,
                cancellationToken).ConfigureAwait(false)
            : await approvalActivationWriter.WriteAsync(
                instance.Id,
                scope.TenantScopeKey,
                nodeKey,
                assignees.Value!.ApprovalPolicy,
                assignees.Value.FallbackAssigneeUserId,
                executionSequence,
                now,
                transition.TimeoutPolicy,
                instance.BusinessType,
                instance.BusinessId,
                cancellationToken).ConfigureAwait(false);
        return Result<Guid>.Success(activation.FirstTodoId);
    }
}

/// <summary>描述一次审批迁移执行后的序号与待办结果。</summary>
/// <param name="NextExecutionSequence">下一个可用执行序号。</param>
/// <param name="FirstTodoId">首个新待办标识；无新待办时为空。</param>
/// <param name="CompletesInstance">迁移是否结束实例。</param>
internal sealed record WorkflowTransitionExecutionResult(
    long NextExecutionSequence,
    Guid? FirstTodoId,
    bool CompletesInstance);

/// <summary>审批迁移执行阶段的稳定错误工厂。</summary>
internal static class WorkflowTransitionExecutionErrors
{
    /// <summary>创建无效迁移错误。</summary>
    public static Error InvalidTransition() =>
        new("workflow.transition.invalid", "The workflow transition could not be executed.", ErrorType.BusinessRule);
}
