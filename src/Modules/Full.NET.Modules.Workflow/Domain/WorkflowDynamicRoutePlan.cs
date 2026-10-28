using System.Collections.ObjectModel;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>将固定路由引用绑定正常图区域；不向正常DAG添加裸回边，不从历史同名节点推断本次激活。</summary>
internal sealed record WorkflowDynamicRoutePlan(IReadOnlyDictionary<string, WorkflowCompiledRoute> Routes)
{
    /// <summary>固定正常图用于核对其他执行帧的结构边界，不从历史节点名反推区域。</summary>
    public WorkflowStructuredRegionPlan? Structure { get; init; }

    public static bool TryCompile(WorkflowDefinitionDraft draft, IReadOnlyDictionary<string, string[]> normalEdges,
        WorkflowFormSchema? schema, out WorkflowDynamicRoutePlan? plan)
    {
        plan = null;
        if (draft.WorkflowKindKey is not ("approval" or "automation") || !WorkflowStructuredRegionPlan.TryCreate(draft, normalEdges, out var regions)) return false;
        var nodes = draft.Nodes.ToDictionary(node => node.NodeKey, StringComparer.Ordinal);
        var routes = new Dictionary<string, WorkflowCompiledRoute>(StringComparer.Ordinal);
        var regionKeys = new HashSet<string>(StringComparer.Ordinal);
        var backRegions = new List<HashSet<string>>();
        foreach (var node in draft.Nodes)
        {
            if (node.NodeTypeKey != WorkflowDynamicRoutePolicy.NodeType)
            {
                if (node.Config.TryGetProperty("routePolicy", out _)) return false;
                continue;
            }
            if (!WorkflowDynamicRoutePolicy.TryRead(node.Config, schema, out var policy) || normalEdges[node.NodeKey] is not [var normalNext] ||
                node.Config.GetProperty("nextNodeKeys")[0].GetString() != normalNext) return false;
            var targets = new List<WorkflowCompiledRouteTarget>();
            foreach (var target in policy!.Targets)
            {
                if (target.RegionKey == "root" || target.RegionKey.StartsWith("return.", StringComparison.Ordinal) || !regionKeys.Add(target.RegionKey) || !nodes.ContainsKey(target.NodeKey)) return false;
                if (regions!.CanForwardTo(node.NodeKey, target.NodeKey)) targets.Add(new(target, false, null));
                else if (regions.TryDescribeRegion(target.NodeKey, node.NodeKey, out var region))
                {
                    var members = region!.NodeKeys.ToHashSet(StringComparer.Ordinal);
                    // 重叠区域只能有明确包含关系；部分交叉会使父代次和退出边界无法唯一确定。
                    if (backRegions.Any(previous => previous.Overlaps(members) && !previous.IsProperSubsetOf(members) && !members.IsProperSubsetOf(previous))) return false;
                    backRegions.Add(members); targets.Add(new(target, true, region));
                }
                else return false;
            }
            routes.Add(node.NodeKey, new(normalNext, policy!, targets.AsReadOnly()));
        }
        if (normalEdges.Values.Sum(value => value.Length) + routes.Values.Sum(route => route.Targets.Count) > WorkflowDefinitionCompiler.MaximumEdges) return false;
        // 正常图上的单入口/出口也约束其他路由引用，不能绕过区域退出点或从区域中途进入。
        foreach (var region in routes.Values.SelectMany(route => route.Targets).Where(target => target.Region is not null).Select(target => target.Region!))
        {
            var members = region.NodeKeys.ToHashSet(StringComparer.Ordinal);
            foreach (var pair in routes)
                foreach (var target in pair.Value.Targets)
                    if (members.Contains(pair.Key) && !members.Contains(target.Target.NodeKey) && pair.Key != region.ExitNodeKey ||
                        !members.Contains(pair.Key) && members.Contains(target.Target.NodeKey) && target.Target.NodeKey != region.EntryNodeKey) return false;
        }
        // 有界状态遍历包含所有允许引用；不能借默认正常路径的人审证明绕过另一条跳转路径。
        var start = draft.Nodes.Single(node => node.NodeTypeKey == "start");
        var pending = new Queue<(string NodeKey, bool HasHuman)>(); var visited = new HashSet<(string NodeKey, bool HasHuman)>();
        pending.Enqueue((start.NodeKey, draft.WorkflowKindKey == "automation"));
        while (pending.TryDequeue(out var state))
        {
            if (!visited.Add(state)) continue;
            var node = nodes[state.NodeKey]; var hasHuman = state.HasHuman || WorkflowRuntimePlan.GuaranteesHumanDecision(node);
            if (node.NodeTypeKey == "end" && !hasHuman) return false;
            foreach (var next in normalEdges[state.NodeKey]) pending.Enqueue((next, hasHuman));
            if (routes.TryGetValue(state.NodeKey, out var route)) foreach (var target in route.Targets) pending.Enqueue((target.Target.NodeKey, hasHuman));
        }
        plan = new(new ReadOnlyDictionary<string, WorkflowCompiledRoute>(routes)) { Structure = regions }; return true;
    }
}

/// <summary>正常出口永远存在；决策失败由运行协议挂起，不能伪装为未命中规则。</summary>
internal sealed record WorkflowCompiledRoute(string NormalNextNodeKey, WorkflowDynamicRoutePolicy Policy, IReadOnlyList<WorkflowCompiledRouteTarget> Targets);
internal sealed record WorkflowCompiledRouteTarget(WorkflowRouteTarget Target, bool Reenters, WorkflowStructuredRegion? Region);
