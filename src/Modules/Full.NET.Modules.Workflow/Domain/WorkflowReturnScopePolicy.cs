using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>候选与提交共用的纯范围裁定；读取方另在可信根锁内复核代次、真实分支及输入修订。</summary>
internal static class WorkflowReturnScopePolicy
{
    public static bool TrySelect(WorkflowInstanceRecord instance, WorkflowRuntimePlan plan, Guid sourceStepId, Guid targetStepId,
        IReadOnlyList<WorkflowReturnStepFact> steps, IReadOnlyList<WorkflowReturnForkFact> forks,
        IReadOnlyList<WorkflowStepCauseRecord> causes, IReadOnlyList<WorkflowActivationBindingRecord> bindings,
        out WorkflowReturnScopeSelection? selection, IReadOnlyList<WorkflowReturnWorkFact>? openWork = null)
    {
        selection = null;
        if (instance.StatusKey != "active" || instance.Id == Guid.Empty || steps.Count is < 1 or > WorkflowActivityScheduler.InstanceActivityBudget ||
            forks.Count > WorkflowActivityScheduler.InstanceActivityBudget || bindings.Count > WorkflowDefinitionCompiler.MaximumNodes ||
            steps.Any(step => step.InstanceId != instance.Id || step.Id == Guid.Empty || step.ExecutionEpochId == Guid.Empty ||
                !WorkflowNodeKeyValidator.IsValid(step.NodeKey) || !FramePair(step.ParallelJoinId, step.ParallelBranchKey)) ||
            forks.Any(fork => fork.InstanceId != instance.Id || fork.Id == Guid.Empty || fork.ExecutionEpochId == Guid.Empty ||
                !FramePair(fork.ParentParallelJoinId, fork.ParentBranchKey)) ||
            steps.Select(step => step.Id).Distinct().Count() != steps.Count || forks.Select(fork => fork.Id).Distinct().Count() != forks.Count) return false;
        var byStep = steps.ToDictionary(step => step.Id); var byFork = forks.ToDictionary(fork => fork.Id);
        openWork ??= [];
        if (openWork.Count > WorkflowActivityScheduler.InstanceActivityBudget || openWork.Select(work => work.Id).Distinct().Count() != openWork.Count ||
            openWork.Any(work => work.InstanceId != instance.Id || work.Id == Guid.Empty || work.ExecutionEpochId == Guid.Empty ||
                !FramePair(work.ParallelJoinId, work.ParallelBranchKey) || work.StatusKey is not ("pending" or "leased" or "faulted")) ||
            forks.Any(fork => fork.ParentParallelJoinId is { } parent && !byFork.ContainsKey(parent))) return false;
        if (!byStep.TryGetValue(sourceStepId, out var source) || !byStep.TryGetValue(targetStepId, out var target) || source.Id == target.Id ||
            source.StatusKey != "active" || target.StatusKey != "completed" ||
            !plan.TryGetReturnNode(source.NodeKey, out var sourceNode) || sourceNode!.NodeTypeKey != source.NodeTypeKey ||
            !plan.TryGetReturnNode(target.NodeKey, out var targetNode) || targetNode!.NodeTypeKey != target.NodeTypeKey ||
            !byStep.TryGetValue(instance.Id, out var start) || start.NodeTypeKey != "start" || start.StatusKey != "completed" || start.ExecutionEpochId != instance.Id ||
            bindings.Any(binding => binding.InstanceId != instance.Id || binding.TenantScopeKey != instance.TenantScopeKey ||
                binding.ExecutionEpochId != source.ExecutionEpochId || !byStep.TryGetValue(binding.StepId, out var step) ||
                step.NodeKey != binding.NodeKey || step.ExecutionEpochId != binding.SourceEpochId || step.StatusKey != "completed") ||
            bindings.Select(binding => binding.NodeKey).Distinct(StringComparer.Ordinal).Count() != bindings.Count ||
            !bindings.Any(binding => binding.StepId == target.Id && binding.NodeKey == target.NodeKey) ||
            !WorkflowCausalGraph.TryReadAncestors(instance.Id, instance.TenantScopeKey, source.Id, causes, out var sourceAncestors) ||
            !sourceAncestors.Contains(target.Id) ||
            !WorkflowCausalGraph.TryReadAncestors(instance.Id, instance.TenantScopeKey, target.Id, causes, out var targetAncestors) ||
            !plan.TryDescribeReturnRegion(target.NodeKey, source.NodeKey, out var region, out var whole)) return false;

        var outerForkId = source.ParallelJoinId; var outerBranch = source.ParallelBranchKey;
        Guid? closingFork = null; var visited = new HashSet<Guid>(); var parentEpoch = source.ExecutionEpochId;
        while (outerForkId != target.ParallelJoinId)
        {
            if (!whole || outerForkId is not { } id || visited.Count >= 8 || !visited.Add(id) || !byFork.TryGetValue(id, out var fork) ||
                fork.StatusKey != "waiting" || !WorkflowNodeKeyValidator.IsValid(outerBranch)) return false;
            closingFork = id; parentEpoch = fork.ExecutionEpochId;
            outerForkId = fork.ParentParallelJoinId; outerBranch = fork.ParentBranchKey;
        }
        if (outerBranch != target.ParallelBranchKey || !whole && (source.ParallelJoinId != target.ParallelJoinId || source.ParallelBranchKey != target.ParallelBranchKey)) return false;
        // ParentEpoch沿真实fork/context选择；读取方另证明它属于来源实际代次祖先且会存活。

        var closedForks = new HashSet<Guid>();
        if (closingFork is { } rootFork)
        {
            var pending = new Queue<Guid>(); pending.Enqueue(rootFork);
            while (pending.TryDequeue(out var id))
            {
                if (!closedForks.Add(id)) return false;
                foreach (var child in forks.Where(fork => fork.ParentParallelJoinId == id)) pending.Enqueue(child.Id);
            }
        }
        var crossed = sourceAncestors.Except(targetAncestors).Append(source.Id).ToHashSet();
        if (!crossed.Contains(target.Id) || crossed.Any(id => !byStep.ContainsKey(id))) return false;
        var affected = steps.Where(step => crossed.Contains(step.Id) || step.ParallelJoinId is { } fork && closedForks.Contains(fork)).ToArray();
        // 未确认或已生效的副作用不因关闭待办而被视为补偿，未知内部活动也失败关闭。
        if (affected.Any(step => !Benign(step.NodeTypeKey) || step.NodeTypeKey == WorkflowDynamicRoutePolicy.NodeType &&
            step.StatusKey is not ("completed" or "rolled_back"))) return false;
        // 外层未收敛生产者不会被新E继承；在它结束前不能让新轮次消费一份缺失的外部依赖。
        var guardFrames = new HashSet<(Guid? Fork, string? Branch)>(); var guardFork = source.ParallelJoinId; var guardBranch = source.ParallelBranchKey;
        var reachesRoot = false;
        for (var depth = 0; depth <= 8; depth++)
        {
            if (!guardFrames.Add((guardFork, guardBranch))) return false;
            if (guardFork is not { } id) { reachesRoot = guardBranch is null; break; }
            if (!byFork.TryGetValue(id, out var fork) || fork.StatusKey != "waiting") return false;
            guardFork = fork.ParentParallelJoinId; guardBranch = fork.ParentBranchKey;
        }
        if (!reachesRoot || steps.Any(step => step.StatusKey == "active" && step.Id != source.Id &&
                guardFrames.Contains((step.ParallelJoinId, step.ParallelBranchKey)) &&
                !(step.ParallelJoinId is { } fork && closedForks.Contains(fork))) ||
            openWork.Any(work => guardFrames.Contains((work.ParallelJoinId, work.ParallelBranchKey)) &&
                !(work.ParallelJoinId is { } fork && closedForks.Contains(fork)))) return false;
        var active = affected.Where(step => step.StatusKey == "active").Select(step => step.Id).ToArray();
        if (!active.Contains(source.Id) || active.Length > WorkflowDefinitionCompiler.MaximumNodes) return false;
        var effective = bindings.Select(binding => binding.StepId).ToHashSet();
        var invalidated = affected.Where(step => step.StatusKey == "completed" && effective.Contains(step.Id)).Select(step => step.Id).ToArray();
        if (!invalidated.Contains(target.Id)) return false;
        // Route的全部允许跳转必须保持在本轮区域内，不能只检查此次碰巧选中的目标。
        if (closedForks.Count > WorkflowDefinitionCompiler.MaximumNodes || invalidated.Length > WorkflowDefinitionCompiler.MaximumNodes ||
            openWork.Count(work => work.ParallelJoinId is { } fork && closedForks.Contains(fork)) > WorkflowDefinitionCompiler.MaximumNodes ||
            !RoutesStayInside(plan, region!)) return false;
        var terminal = plan.IsTerminalNode(region!.ExitNodeKey);
        string? next = region!.ExitNodeKey;
        if (!terminal && !plan.TryGetNormalSuccessor(region.ExitNodeKey, out next)) return false;
        selection = new(region, parentEpoch, target.ParallelJoinId, target.ParallelBranchKey, closedForks.Order().ToArray(),
            active.Order().ToArray(), invalidated.Order().ToArray(), next!, terminal,
            openWork.Where(work => work.ParallelJoinId is { } fork && closedForks.Contains(fork)).Select(work => work.Id).Order().ToArray());
        return true;
    }

    private static bool RoutesStayInside(WorkflowRuntimePlan plan, WorkflowStructuredRegion region)
    {
        var members = region.NodeKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in region.NodeKeys.Where(key => plan.FindNodeTypeKey(key) == WorkflowDynamicRoutePolicy.NodeType))
        {
            if (!plan.DynamicRoutes.Routes.TryGetValue(key, out var route) || !members.Contains(route.NormalNextNodeKey) ||
                route.Targets.Any(target => !members.Contains(target.Target.NodeKey) || target.Reenters &&
                    (target.Region is null || !target.Region.NodeKeys.ToHashSet(StringComparer.Ordinal).IsProperSubsetOf(members)))) return false;
        }
        return true;
    }

    private static bool FramePair(Guid? fork, string? branch) => fork is null ? branch is null : fork != Guid.Empty && WorkflowNodeKeyValidator.IsValid(branch);
    private static bool Benign(string type) => type is "start" or "end" or "control.empty" or "notify.cc" or "timer.delay" or
        "human.approval" or "human.group_approval" or "human.task" or "human.vote" or "human.claim" or
        "gateway.exclusive" or "gateway.parallel" or "gateway.inclusive" or WorkflowDynamicRoutePolicy.NodeType;
}

/// <summary>只读取实际执行身份和状态，执行序号不参与退回授权。</summary>
internal sealed record WorkflowReturnStepFact(Guid Id, Guid InstanceId, Guid ExecutionEpochId, string NodeKey, string NodeTypeKey,
    string StatusKey, Guid? ParallelJoinId, string? ParallelBranchKey);

/// <summary>真实fork/context形成父子树；节点同名不能代替本次分叉身份。</summary>
internal sealed record WorkflowReturnForkFact(Guid Id, Guid InstanceId, Guid ExecutionEpochId, string ForkNodeKey, string JoinNodeKey,
    string StatusKey, Guid? ParentParallelJoinId, string? ParentBranchKey);

/// <summary>只关闭裁定的活动集合；历史完成事实经有效绑定失效，兄弟分支与无关字段保持独立。</summary>
internal sealed record WorkflowReturnScopeSelection(WorkflowStructuredRegion Region, Guid ParentEpochId, Guid? ParallelJoinId,
    string? ParallelBranchKey, IReadOnlyList<Guid> ClosedForkIds, IReadOnlyList<Guid> ActiveStepIds, IReadOnlyList<Guid> InvalidatedStepIds,
    string NextNodeKey, bool ExitsAtRootEnd, IReadOnlyList<Guid> ClosedWorkIds);

/// <summary>原工作身份与实际作用域用于判断是否可关闭；字段值不参与范围授权。</summary>
internal sealed record WorkflowReturnWorkFact(Guid Id, Guid InstanceId, Guid ExecutionEpochId, Guid? ParallelJoinId,
    string? ParallelBranchKey, string NodeKey, string StatusKey);
