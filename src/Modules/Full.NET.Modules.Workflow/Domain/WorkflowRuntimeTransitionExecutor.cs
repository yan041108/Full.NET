using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Execution.Connectors;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在 Workflow 本地事务中执行审批迁移，包括并行分叉、汇合与下一审批激活。</summary>
/// <param name="automaticTransitionWriter">自动节点写入器。</param>
/// <param name="humanActivationWriter">审批激活写入器。</param>
/// <param name="parallelJoinCoordinator">并行汇合协调器。</param>
/// <param name="queryExecutor">显式 SQL 查询执行器。</param>
/// <param name="commandExecutor">在同一根事务中关闭已收敛的执行代次。</param>
/// <param name="activityScheduler">事务预算用尽后的耐久调度器。</param>
/// <param name="branchStates">分叉输入快照与局部输出协调器。</param>
internal sealed partial class WorkflowRuntimeTransitionExecutor(
    WorkflowAutomaticTransitionWriter automaticTransitionWriter,
    WorkflowHumanActivityActivationWriter humanActivationWriter,
    WorkflowParallelJoinCoordinator parallelJoinCoordinator,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    WorkflowActivityScheduler activityScheduler,
    WorkflowBranchStateCoordinator branchStates, WorkflowConnectorCatalog connectors, WorkflowHumanActivityCompletionCoordinator callbacks,
    WorkflowBusinessCommandActivationWriter businessCommands, WorkflowSubprocessActivationWriter subprocesses, WorkflowReturnRoundCoordinator returnRounds)
{
    /// <summary>执行一次闭合审批迁移并返回首个新待办标识（若存在）。</summary>
    /// <param name="instance">当前实例。</param>
    /// <param name="scope">可信作用域。</param>
    /// <param name="executionEpochId">沿真实来源步骤或耐久工作传递的执行代次。</param>
    /// <param name="runtimePlan">发布定义运行计划。</param>
    /// <param name="transition">待执行的闭合迁移。</param>
    /// <param name="values">实例当前表单值。</param>
    /// <param name="executionSequence">首个自动步骤可用执行序号。</param>
    /// <param name="now">业务发生时间。</param>
    /// <param name="startedById">实例发起人，用于办理人解析。</param>
    /// <param name="activeParallelJoinId">当前分支所属汇合状态；汇合到达时必填。</param>
    /// <param name="activeParallelBranchKey">当前分支键；并行分支审批时必填。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="budget">当前事务各分支共享的活动预算。</param>
    /// <param name="preparedAssignees">在事务外解析的本轮办理人快照。</param>
    /// <returns>执行结果或稳定错误。</returns>
    public async Task<Result<WorkflowTransitionExecutionResult>> ExecuteAsync(
        WorkflowInstanceRecord instance,
        WorkflowManagementScope scope,
        Guid executionEpochId,
        WorkflowRuntimePlan runtimePlan,
        WorkflowRuntimeTransition transition,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values,
        long executionSequence,
        DateTimeOffset now,
        Guid startedById,
        Guid? activeParallelJoinId = null,
        string? activeParallelBranchKey = null,
        CancellationToken cancellationToken = default,
        WorkflowDispatchBudget? budget = null,
        WorkflowPreparedHumanActivities? preparedAssignees = null, WorkflowActivationTrace? trace = null, Guid? sourceStepId = null)
    {
        if (scope.TenantId != instance.TenantId || scope.ScopeKey != instance.ScopeKey || scope.TenantScopeKey != instance.TenantScopeKey)
            return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
        if (trace is null && sourceStepId is { } source && source!=Guid.Empty)
            trace=new(source,activeParallelJoinId,activeParallelBranchKey);
        if (trace is null) return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
        if (budget is null || budget.TotalRemaining < 0)
        {
            var count = await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowActivitySql.CountActivations,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id)), cancellationToken).ConfigureAwait(false);
            budget ??= new WorkflowDispatchBudget();
            budget.TotalRemaining = await WorkflowExecutionBudgetPolicy.ReadRemainingAsync(instance, runtimePlan.HasSubprocess, count,
                queryExecutor, cancellationToken).ConfigureAwait(false);
            if (budget.TotalRemaining < 0) return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
        }
        if (budget.ExecutionEpochId == Guid.Empty && !await WorkflowExecutionFrameScope.IsValidAsync(queryExecutor, instance,
                executionEpochId, activeParallelJoinId, activeParallelBranchKey, cancellationToken).ConfigureAwait(false) || !budget.TryBindExecutionEpoch(executionEpochId))
            return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
        var returnExit = await returnRounds.TryExitAsync(instance, executionEpochId, runtimePlan, trace, budget,
            activeParallelJoinId, activeParallelBranchKey, values, executionSequence, now, cancellationToken).ConfigureAwait(false);
        if (returnExit is not null) return returnExit;
        var available = budget.TimeExpired ? 0 : Math.Max(0, Math.Min(budget.Remaining, budget.TotalRemaining));
        if (transition.CompletesInstance && (transition.TerminalNodeKey is not { } terminal || !runtimePlan.IsTerminalNode(terminal)))
            return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
        var automaticNodes = transition.CompletesInstance
            ? transition.AutomaticNodes.Append(new WorkflowAutomaticRuntimeNode(transition.TerminalNodeKey!, "end", [], null)).ToArray()
            : transition.AutomaticNodes.ToArray();
        var automaticCount = Math.Min(automaticNodes.Length, available);
        for (var index = 0; index < automaticCount; index++)
        {
            var node = automaticNodes[index];
            if (node.RecipientPolicy is null) continue;
            // 前序办理尚未提交或超出本轮准备上限时，先保存前缀并在该抄送节点耐久续办。
            if (preparedAssignees is null || !preparedAssignees.TryGetCc(node.NodeKey, out var recipients)) { automaticCount = index; break; }
            if (!recipients.IsSuccess) return Result<WorkflowTransitionExecutionResult>.Failure(recipients.Error!);
            automaticNodes[index] = node with { RecipientUserIds = recipients.Value!.UserIds,
                ResolutionJson = recipients.Value.ResolutionJson, ResolutionHash = recipients.Value.ResolutionHash };
        }
        if (automaticCount < automaticNodes.Length ||
            (automaticCount == available && (transition.NextHumanNodeKey is not null || transition.WaitsAtJoin || transition.AutomaticWait is not null)))
        {
            var nextSequence = await automaticTransitionWriter.WriteAsync(instance.Id, scope.TenantScopeKey, budget.ExecutionEpochId,
                automaticNodes.Take(automaticCount).ToArray(), executionSequence, now,
                cancellationToken, trace: trace).ConfigureAwait(false);
            budget.Remaining -= automaticCount;
            budget.TotalRemaining -= automaticCount;
            budget.Exhausted |= budget.TotalRemaining == 0;
            var nodeKey = automaticCount < automaticNodes.Length
                ? automaticNodes[automaticCount].NodeKey : transition.NextHumanNodeKey ?? transition.AutomaticWait?.NodeKey ?? transition.JoinArrival!.JoinNodeKey;
            var revision = await branchStates.ReadRevisionAsync(instance, activeParallelJoinId,
                activeParallelBranchKey, cancellationToken).ConfigureAwait(false);
            if (revision is null) return Result<WorkflowTransitionExecutionResult>.Failure(
                WorkflowTransitionExecutionErrors.InvalidTransition());
            await activityScheduler.EnqueueAsync(instance, budget.ExecutionEpochId, nodeKey, values, revision.Value, nextSequence,
                activeParallelJoinId, activeParallelBranchKey, now, cancellationToken, budget.Exhausted, sourceStepId: trace.LastStepId,causeKindKey:trace.KindKey).ConfigureAwait(false);
            return Result<WorkflowTransitionExecutionResult>.Success(new(nextSequence, null, false, budget.Exhausted));
        }
        budget.Remaining -= automaticCount;
        budget.TotalRemaining -= automaticCount;
        var sequence = await automaticTransitionWriter.WriteAsync(
            instance.Id,
            scope.TenantScopeKey,
            budget.ExecutionEpochId,
            automaticNodes,
            executionSequence,
            now,
            cancellationToken, trace: trace).ConfigureAwait(false);

        if (transition.CompletesInstance)
        {
            // 共同End可能是退回轮次出口；先持久化真实End，再用原收据恢复外层，根终态仍只有一次。
            var terminalReturn = await returnRounds.TryExitAsync(instance, executionEpochId, runtimePlan, trace, budget,
                activeParallelJoinId, activeParallelBranchKey, values, sequence, now, cancellationToken).ConfigureAwait(false);
            if (terminalReturn is not null) return terminalReturn;
        }

        if (transition.AutomaticWait is { } automaticWait)
        {
            var revision = await branchStates.ReadRevisionAsync(instance, activeParallelJoinId,
                activeParallelBranchKey, cancellationToken).ConfigureAwait(false);
            if (revision is null) return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            budget.Remaining--; budget.TotalRemaining--;
            if (automaticWait is WorkflowAsyncBarrierWaitPlan barrier)
            {
                var calls = await queryExecutor.QueryAsync<WorkflowConnectorCallRecord>(WorkflowConnectorSql.FindInFrame,
                    WorkflowSqlParameters.Create(("InstanceId",instance.Id),("TenantScopeKey",scope.TenantScopeKey),("NodeKeys",barrier.Definition.ProducerNodeKeys.ToArray()),("ExecutionEpochId",budget.ExecutionEpochId),
                        ("ParallelJoinId",activeParallelJoinId),("ParallelBranchKey",activeParallelBranchKey)),cancellationToken).ConfigureAwait(false);
                if (calls.Count > WorkflowDefinitionCompiler.MaximumNodes || calls.GroupBy(call=>call.NodeKey,StringComparer.Ordinal).Any(group=>group.Count()!=1) ||
                    !barrier.Definition.IsImplicitBoundary && barrier.Definition.ProducerNodeKeys.Any(key=>!calls.Any(call=>call.NodeKey==key)))
                    return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
                await activityScheduler.EnqueueBarrierAsync(instance, budget.ExecutionEpochId,barrier,calls.Select(call=>call.Id).ToArray(),values,revision.Value,sequence,
                    activeParallelJoinId,activeParallelBranchKey,now,cancellationToken, trace: trace).ConfigureAwait(false);
            }
            else if (automaticWait is WorkflowDelayWaitPlan delay)
            {
                if (!delay.DelayPolicy.TryResolve(values, now, out var due))
                    return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
                await activityScheduler.EnqueueDelayAsync(instance, budget.ExecutionEpochId, delay, values, revision.Value, sequence,
                    activeParallelJoinId, activeParallelBranchKey, due, now, cancellationToken, trace: trace).ConfigureAwait(false);
            }
            else if (automaticWait is WorkflowFieldWaitPlan or WorkflowRouteWaitPlan)
                await activityScheduler.EnqueueAsync(instance, budget.ExecutionEpochId, automaticWait.NodeKey, values, revision.Value, sequence,
                    activeParallelJoinId, activeParallelBranchKey, now, cancellationToken, sourceStepId: trace.LastStepId,causeKindKey:trace.KindKey).ConfigureAwait(false);
            else if (automaticWait is WorkflowConnectorWaitPlan connectorWait)
            {
                var connector = connectors.Find(scope.TenantScopeKey, connectorWait.Policy.ConnectorKey,
                    connectorWait.Policy.ConnectorVersion, connectorWait.ConnectorHash);
                if (connector is null || !connectorWait.Policy.TryCreateInput(values, out var requestJson))
                    return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
                await activityScheduler.EnqueueConnectorAsync(instance, budget.ExecutionEpochId, connectorWait, connector, values, requestJson!, revision.Value, sequence,
                    activeParallelJoinId, activeParallelBranchKey, now, cancellationToken, trace: trace).ConfigureAwait(false);
                if (connectorWait.IsAsync)
                    await activityScheduler.EnqueueAsync(instance, budget.ExecutionEpochId,connectorWait.ContinuationNodeKey,values,revision.Value,sequence+1,
                        activeParallelJoinId,activeParallelBranchKey,now,cancellationToken, sourceStepId: trace.LastStepId,causeKindKey:trace.KindKey).ConfigureAwait(false);
            }
            else if (automaticWait is WorkflowBusinessCommandWaitPlan businessWait)
            {
                var activation = await businessCommands.ActivateAsync(instance, scope, budget.ExecutionEpochId, businessWait, sequence, activeParallelJoinId,
                    activeParallelBranchKey, now, cancellationToken, trace: trace).ConfigureAwait(false);
                if (!activation.IsSuccess) return Result<WorkflowTransitionExecutionResult>.Failure(activation.Error!);
            }
            else if (automaticWait is WorkflowSubprocessWaitPlan subprocessWait)
            {
                var activation = await subprocesses.ActivateAsync(instance, scope, budget.ExecutionEpochId, subprocessWait, values, revision.Value, sequence,
                    activeParallelJoinId, activeParallelBranchKey, now, cancellationToken, trace: trace).ConfigureAwait(false);
                if (!activation.IsSuccess) return Result<WorkflowTransitionExecutionResult>.Failure(activation.Error!);
                budget.TotalRemaining -= activation.Value;
            }
            else return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            return Result<WorkflowTransitionExecutionResult>.Success(new(sequence + 1, null, false));
        }

        if (transition.ParallelFork is { } fork)
        {
            return await ExecuteParallelForkAsync(
                instance, scope, runtimePlan, values, fork, sequence, now, startedById, activeParallelJoinId,
                activeParallelBranchKey, cancellationToken, budget, preparedAssignees, trace)
                .ConfigureAwait(false);
        }

        if (transition.WaitsAtJoin && transition.JoinArrival is { } joinArrival &&
            (joinArrival.BranchKey is { Length: > 0 } explicitBranch ? explicitBranch : activeParallelBranchKey) is { Length: > 0 } branchKey)
        {
            budget.Remaining--;
            budget.TotalRemaining--;
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
                cancellationToken, budget, preparedAssignees, trace).ConfigureAwait(false);
        }

        if (transition.CompletesInstance)
        {
            if (runtimePlan.HasSubprocess && await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowSubprocessSql.CountUnsettled,
                    WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false) != 0)
                return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            if (runtimePlan.HasBusinessCommands && await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowBusinessCommandSql.CountUnsettled,
                    WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false) != 0)
                return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            if (await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowConnectorSql.CountUnsettled,
                    WorkflowSqlParameters.Create(("InstanceId",instance.Id),("TenantScopeKey",scope.TenantScopeKey)),cancellationToken).ConfigureAwait(false)!=0)
                return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            await commandExecutor.ExecuteAsync(WorkflowExecutionEpochSql.CloseInstance,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", scope.TenantScopeKey), ("Now", now)), cancellationToken).ConfigureAwait(false);
            return Result<WorkflowTransitionExecutionResult>.Success(
                new WorkflowTransitionExecutionResult(sequence, null, true));
        }

        if (transition.NextHumanNodeKey is not { } nextHumanNodeKey)
        {
            return Result<WorkflowTransitionExecutionResult>.Failure(
                WorkflowTransitionExecutionErrors.InvalidTransition());
        }

        if (preparedAssignees is null || !preparedAssignees.TryGet(nextHumanNodeKey, out var assignees))
        {
            var revision = await branchStates.ReadRevisionAsync(instance, activeParallelJoinId,
                activeParallelBranchKey, cancellationToken).ConfigureAwait(false);
            if (revision is null) return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            await activityScheduler.EnqueueAsync(instance, budget.ExecutionEpochId, nextHumanNodeKey, values, revision.Value, sequence,
                activeParallelJoinId, activeParallelBranchKey, now, cancellationToken, sourceStepId: trace.LastStepId,causeKindKey:trace.KindKey).ConfigureAwait(false);
            return Result<WorkflowTransitionExecutionResult>.Success(new(sequence, null, false));
        }
        budget.Remaining--;
        budget.TotalRemaining--;
        if (assignees.IsSuccess && assignees.Value!.Handling is not null)
            return await ExecuteHandlingAsync(instance, scope, runtimePlan, nextHumanNodeKey, assignees.Value,
                values, sequence, now, startedById, activeParallelJoinId, activeParallelBranchKey,
                cancellationToken, budget, preparedAssignees, trace).ConfigureAwait(false);
        var nextTodoId = await ActivateHumanAsync(
            instance,
            scope,
            budget.ExecutionEpochId,
            transition,
            nextHumanNodeKey,
            activeParallelJoinId,
            activeParallelBranchKey,
            sequence,
            now,
            assignees, values, runtimePlan.FindSlaPolicy(nextHumanNodeKey), cancellationToken, trace).ConfigureAwait(false);
        if (!nextTodoId.IsSuccess)
        {
            return Result<WorkflowTransitionExecutionResult>.Failure(nextTodoId.Error!);
        }

        return Result<WorkflowTransitionExecutionResult>.Success(
            new WorkflowTransitionExecutionResult(sequence + 1, nextTodoId.Value, false));
    }

    /// <summary>执行并行分叉并同时激活各分支首个等待点。</summary>
    private async Task<Result<WorkflowTransitionExecutionResult>> ExecuteParallelForkAsync(
        WorkflowInstanceRecord instance,
        WorkflowManagementScope scope,
        WorkflowRuntimePlan runtimePlan,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values,
        WorkflowParallelForkPlan fork,
        long executionSequence,
        DateTimeOffset now,
        Guid startedById,
        Guid? parentJoinId,
        string? parentBranchKey,
        CancellationToken cancellationToken,
        WorkflowDispatchBudget budget,
        WorkflowPreparedHumanActivities? preparedAssignees, WorkflowActivationTrace trace)
    {
        var joinId = await parallelJoinCoordinator.CreateJoinAsync(
            instance.Id,
            budget.ExecutionEpochId,
            fork.ForkNodeKey,
            fork.JoinNodeKey,
            fork.Branches.Count,
            now,
            fork.GatewayTypeKey,
            cancellationToken, forkStepId: trace.LastStepId).ConfigureAwait(false);
        await branchStates.CreateAsync(joinId, instance, fork.Branches, values, parentJoinId, parentBranchKey, cancellationToken).ConfigureAwait(false);
        var sequence = executionSequence;
        var completesInstance = false;
        var suspendsInstance = false;
        Guid? firstTodoId = null;
        foreach (var branch in fork.Branches)
        {
            var result = await ExecuteAsync(
                instance, scope, budget.ExecutionEpochId, runtimePlan,
                new WorkflowRuntimeTransition(branch.NextHumanNodeKey, branch.CompletesInstance,
                    branch.AutomaticNodes, branch.TimeoutPolicy, branch.ApprovalPolicy,
                    branch.AssigneePolicy, parallelFork: branch.ParallelFork, joinArrival: branch.JoinArrival,
                    taskPolicy: branch.TaskPolicy, votePolicy: branch.VotePolicy, claimPolicy: branch.ClaimPolicy,
                    automaticWait: branch.AutomaticWait, terminalNodeKey: branch.TerminalNodeKey),
                values, sequence, now, startedById, joinId, branch.BranchKey, cancellationToken, budget, preparedAssignees, trace: trace.Fork(joinId,branch.BranchKey))
                .ConfigureAwait(false);
            if (!result.IsSuccess) return result;
            if (result.Value!.RejectsInstance) return result;
            sequence = result.Value!.NextExecutionSequence;
            firstTodoId ??= result.Value.FirstTodoId;
            completesInstance |= result.Value.CompletesInstance;
            suspendsInstance |= result.Value.SuspendsInstance;
        }

        return Result<WorkflowTransitionExecutionResult>.Success(
            new WorkflowTransitionExecutionResult(sequence, firstTodoId, completesInstance, budget.Exhausted || suspendsInstance));
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
        CancellationToken cancellationToken,
        WorkflowDispatchBudget budget,
        WorkflowPreparedHumanActivities? preparedAssignees, WorkflowActivationTrace trace)
    {
        var parallelJoinId = activeParallelJoinId;
        if (parallelJoinId is null || parallelJoinId == Guid.Empty)
        {
            return Result<WorkflowTransitionExecutionResult>.Failure(
                WorkflowTransitionExecutionErrors.InvalidTransition());
        }

        var join = await parallelJoinCoordinator.TryRecordArrivalAsync(
            instance.Id,
            budget.ExecutionEpochId,
            parallelJoinId.Value,
            joinArrival.ForkNodeKey,
            joinArrival.JoinNodeKey,
            branchKey,
            now,
            cancellationToken, sourceStepId: trace.LastStepId, tenantScopeKey: scope.TenantScopeKey, routeProof: trace.RouteProof, returnProof: trace.ReturnProof).ConfigureAwait(false);
        if (!join.IsValid) return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
        if (!join.IsNewArrival) return Result<WorkflowTransitionExecutionResult>.Success(new(executionSequence, null, false));

        var context = join.IsJoinComplete
            ? await branchStates.ReadContextAsync(instance,parallelJoinId.Value,cancellationToken).ConfigureAwait(false) : null;
        if (join.IsJoinComplete)
        {
            var sources=await queryExecutor.QueryAsync<Guid?>(WorkflowCausalitySql.ArrivalSources,
                WorkflowSqlParameters.Create(("InstanceId",instance.Id),("TenantScopeKey",scope.TenantScopeKey),("ParallelJoinId",parallelJoinId.Value)),cancellationToken).ConfigureAwait(false);
            if (sources.Count!=join.Join!.RequiredBranchCount) return Result<WorkflowTransitionExecutionResult>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            trace.Join(sources,context?.ParentParallelJoinId,context?.ParentBranchKey);
        }
        else trace.Join(new Guid?[]{trace.LastStepId},parallelJoinId,branchKey);

        var gatewayNodeType = joinArrival.ForkNodeKey is not null &&
            runtimePlan.IsInclusiveFork(joinArrival.ForkNodeKey)
                ? "gateway.inclusive"
                : "gateway.parallel";
        var sequence = await automaticTransitionWriter.WriteAsync(
            instance.Id,
            scope.TenantScopeKey,
            budget.ExecutionEpochId,
            [
                new WorkflowAutomaticRuntimeNode(
                    joinArrival.JoinNodeKey,
                    gatewayNodeType,
                    [],
                    "joined"),
            ],
            executionSequence,
            now,
            cancellationToken, trace: trace).ConfigureAwait(false);

        if (!join.IsJoinComplete)
        {
            return Result<WorkflowTransitionExecutionResult>.Success(
                new WorkflowTransitionExecutionResult(sequence, null, false));
        }

        var merge = await branchStates.MergeAsync(instance, parallelJoinId.Value, startedById, now, cancellationToken)
            .ConfigureAwait(false);
        if (!merge.IsSuccess) return Result<WorkflowTransitionExecutionResult>.Failure(merge.Error!);
        values = merge.Value!;


        var parentJoin = context?.ParentParallelJoinId is { } parent
            ? await queryExecutor.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById,
                WorkflowSqlParameters.Create(("Id", parent), ("InstanceId", instance.Id)), cancellationToken).ConfigureAwait(false)
            : null;
        if (!runtimePlan.TryResolveAfterJoin(joinArrival.JoinNodeKey, values, out var afterJoin, parentJoin?.JoinNodeKey))
        {
            return Result<WorkflowTransitionExecutionResult>.Failure(
                WorkflowTransitionExecutionErrors.InvalidTransition());
        }

        return await ExecuteAsync(
            instance,
            scope,
            budget.ExecutionEpochId,
            runtimePlan,
            afterJoin,
            values,
            sequence,
            now,
            startedById,
            activeParallelJoinId: context?.ParentParallelJoinId, activeParallelBranchKey: context?.ParentBranchKey,
            cancellationToken: cancellationToken, budget: budget, preparedAssignees: preparedAssignees, trace: trace).ConfigureAwait(false);
    }

    /// <summary>激活下一人工审批并返回首个待办标识。</summary>
    private async Task<Result<Guid?>> ActivateHumanAsync(
        WorkflowInstanceRecord instance,
        WorkflowManagementScope scope,
        Guid executionEpochId,
        WorkflowRuntimeTransition transition,
        string nodeKey,
        Guid? parallelJoinId,
        string? parallelBranchKey,
        long executionSequence,
        DateTimeOffset now,
        Result<WorkflowHumanActivityAssignees> assignees,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> values,
        WorkflowSlaPolicy? slaPolicy,
        CancellationToken cancellationToken, WorkflowActivationTrace trace)
    {
        if (!assignees.IsSuccess)
        {
            return Result<Guid?>.Failure(assignees.Error!);
        }

        if (transition.ClaimPolicy is not null || assignees.Value!.Claim is not null)
        {
            if (transition.ClaimPolicy is null || assignees.Value!.Claim is not { } claim || claim.Policy != transition.ClaimPolicy)
                return Result<Guid?>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            var claimStep = await humanActivationWriter.WriteClaimAsync(instance.Id, scope.TenantScopeKey, executionEpochId, nodeKey, claim,
                executionSequence, now, transition.TimeoutPolicy, parallelJoinId, parallelBranchKey,
                assignees.Value.ResolutionJson, assignees.Value.ResolutionHash, cancellationToken, trace: trace).ConfigureAwait(false);
            await humanActivationWriter.WriteSlaAsync(claimStep, instance.Id, scope.TenantScopeKey, slaPolicy, now, cancellationToken).ConfigureAwait(false);
            return Result<Guid?>.Success(null);
        }
        if (transition.VotePolicy is not null || assignees.Value!.Vote is not null)
        {
            if (transition.VotePolicy is null || assignees.Value!.Vote is not { } vote)
                return Result<Guid?>.Failure(new(Contracts.WorkflowErrorCodes.SchemaInvalid, "The vote preparation is inconsistent.", ErrorType.BusinessRule));
            var binding = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowVoteFormBindingRecord>(WorkflowVoteSql.FindFormBinding,
                WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("NodeKey", nodeKey),
                    ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
            if (binding is null || binding.FormStatusKey != "active" || binding.BallotFormVersionId != transition.VotePolicy.BallotFormVersionId ||
                binding.FormSchemaHash != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(binding.FormSchemaJson))))
                return Result<Guid?>.Failure(new(Contracts.WorkflowErrorCodes.SchemaInvalid, "The pinned ballot form is unavailable.", ErrorType.BusinessRule));
            var voteActivation = await humanActivationWriter.WriteVoteAsync(instance.Id, scope.TenantScopeKey, executionEpochId, nodeKey,
                vote with { Policy = transition.VotePolicy }, binding,
                executionSequence, now, transition.TimeoutPolicy, instance.BusinessType, instance.BusinessId, parallelJoinId,
                parallelBranchKey, assignees.Value.ResolutionJson, assignees.Value.ResolutionHash, cancellationToken, trace: trace).ConfigureAwait(false);
            await humanActivationWriter.CaptureInputsAsync(voteActivation, instance.Id, scope.TenantScopeKey, values, cancellationToken).ConfigureAwait(false);
            await humanActivationWriter.WriteSlaAsync(voteActivation.StepId, instance.Id, scope.TenantScopeKey, slaPolicy, now, cancellationToken).ConfigureAwait(false);
            return Result<Guid?>.Success(voteActivation.FirstTodoId);
        }
        if (transition.TaskPolicy is not null || assignees.Value!.Task is not null)
        {
            if (transition.TaskPolicy is null || assignees.Value!.Task is not { } task)
                return Result<Guid?>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
            var taskActivation = await humanActivationWriter.WriteTaskAsync(instance.Id, scope.TenantScopeKey, executionEpochId, nodeKey, task,
                executionSequence, now, transition.TimeoutPolicy, instance.BusinessType, instance.BusinessId,
                parallelJoinId, parallelBranchKey, assignees.Value.ResolutionJson, assignees.Value.ResolutionHash, cancellationToken, trace: trace).ConfigureAwait(false);
            await humanActivationWriter.CaptureInputsAsync(taskActivation, instance.Id, scope.TenantScopeKey, values, cancellationToken).ConfigureAwait(false);
            await humanActivationWriter.WriteSlaAsync(taskActivation.StepId, instance.Id, scope.TenantScopeKey, slaPolicy, now, cancellationToken).ConfigureAwait(false);
            return Result<Guid?>.Success(taskActivation.FirstTodoId);
        }
        var activation = parallelJoinId is { } joinId && parallelBranchKey is not null
            ? await humanActivationWriter.WriteParallelAsync(
                instance.Id,
                scope.TenantScopeKey,
                executionEpochId,
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
                cancellationToken, assignees.Value.ResolutionJson, assignees.Value.ResolutionHash, trace: trace).ConfigureAwait(false)
            : await humanActivationWriter.WriteAsync(
                instance.Id,
                scope.TenantScopeKey,
                executionEpochId,
                nodeKey,
                assignees.Value!.ApprovalPolicy,
                assignees.Value.FallbackAssigneeUserId,
                executionSequence,
                now,
                transition.TimeoutPolicy,
                instance.BusinessType,
                instance.BusinessId,
                cancellationToken, assignees.Value.ResolutionJson, assignees.Value.ResolutionHash, trace: trace).ConfigureAwait(false);
        if (assignees.Value.ApprovalPolicy?.Groups is { } groups && groups.Any(group => group.Handling is not null))
            await humanActivationWriter.RecordGroupHandlingAsync(instance, scope, activation.StepId, groups,
                assignees.Value.ResolutionJson!, now, cancellationToken).ConfigureAwait(false);
        await humanActivationWriter.CaptureInputsAsync(activation, instance.Id, scope.TenantScopeKey, values, cancellationToken).ConfigureAwait(false);
        await humanActivationWriter.WriteSlaAsync(activation.StepId, instance.Id, scope.TenantScopeKey, slaPolicy, now, cancellationToken).ConfigureAwait(false);
        return Result<Guid?>.Success(activation.FirstTodoId);
    }
}

/// <summary>描述一次审批迁移执行后的序号与待办结果。</summary>
/// <param name="NextExecutionSequence">下一个可用执行序号。</param>
/// <param name="FirstTodoId">首个新待办标识；无新待办时为空。</param>
/// <param name="CompletesInstance">迁移是否结束实例。</param>
/// <param name="SuspendsInstance">总活动预算耗尽时保留续办事实并暂停实例。</param>
internal sealed record WorkflowTransitionExecutionResult(
    long NextExecutionSequence,
    Guid? FirstTodoId,
    bool CompletesInstance,
    bool SuspendsInstance = false,
    bool RejectsInstance = false)
{
    public string InstanceStatusKey => RejectsInstance ? "rejected" : SuspendsInstance ? "suspended" : CompletesInstance ? "completed" : "active";
}

/// <summary>审批迁移执行阶段的稳定错误工厂。</summary>
internal static class WorkflowTransitionExecutionErrors
{
    /// <summary>创建无效迁移错误。</summary>
    public static Error InvalidTransition() =>
        new("workflow.transition.invalid", "The workflow transition could not be executed.", ErrorType.BusinessRule);
}
