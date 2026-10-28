namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在正常DAG上编译结构作用域及单入口/出口区域；跳转引用不参与正常边遍历。</summary>
internal sealed class WorkflowStructuredRegionPlan
{
    private readonly IReadOnlyDictionary<string, string[]> edges;
    private readonly IReadOnlyDictionary<string, HashSet<string>> dominators;
    private readonly IReadOnlyDictionary<string, HashSet<string>> postDominators;
    private readonly IReadOnlyDictionary<string, string> scopes;
    private readonly IReadOnlySet<string> forbiddenEntries;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Frame>> scopePaths;

    private WorkflowStructuredRegionPlan(IReadOnlyDictionary<string, string[]> edges,
        IReadOnlyDictionary<string, HashSet<string>> dominators,
        IReadOnlyDictionary<string, HashSet<string>> postDominators, IReadOnlyDictionary<string, string> scopes, IReadOnlySet<string> forbiddenEntries,
        IReadOnlyDictionary<string, IReadOnlyList<Frame>> scopePaths)
    {
        this.edges = edges; this.dominators = dominators; this.postDominators = postDominators; this.scopes = scopes;
        this.forbiddenEntries = forbiddenEntries;
        this.scopePaths = scopePaths;
    }

    public bool CanForwardTo(string source, string target) => source != target && !forbiddenEntries.Contains(target) && SameScope(source, target) && Reach(source).Contains(target);

    public bool TryDescribeRegion(string entry, string exit, out WorkflowStructuredRegion? region)
    {
        region = null;
        if (entry == exit || forbiddenEntries.Contains(entry) || !SameScope(entry, exit) || !dominators[exit].Contains(entry) || !postDominators[entry].Contains(exit)) return false;
        var reachable = Reach(entry);
        var members = reachable.Where(key => postDominators[key].Contains(exit)).ToHashSet(StringComparer.Ordinal);
        if (!members.Contains(exit)) return false;
        // 区域只在入口接收外边、在出口离开；不能以可达性掩盖另一个入口或提前出口。
        foreach (var pair in edges)
            foreach (var target in pair.Value)
                if ((!members.Contains(pair.Key) && members.Contains(target) && target != entry) ||
                    (members.Contains(pair.Key) && !members.Contains(target) && pair.Key != exit)) return false;
        region = new(entry, exit, Array.AsReadOnly(members.Order(StringComparer.Ordinal).ToArray()));
        return true;
    }

    /// <summary>退回越出当前分支时，出口必须提升到实际外层汇合；兄弟分支不能成为裸跳转目标。</summary>
    public bool TryDescribeReturnRegion(string entry, string source, out WorkflowStructuredRegion? region, out bool closesWholeRegion)
    {
        region = null;
        closesWholeRegion = false;
        if (!scopePaths.TryGetValue(entry, out var targetPath) || !scopePaths.TryGetValue(source, out var sourcePath)) return false;
        if (SameScope(entry, source)) return TryDescribeRegion(entry, source, out region);
        if (targetPath.Count >= sourcePath.Count || !targetPath.SequenceEqual(sourcePath.Take(targetPath.Count))) return false;
        var boundary = sourcePath[targetPath.Count];
        if (boundary.JoinKey is not { } exit || !TryDescribeRegion(entry, exit, out region) ||
            !region!.NodeKeys.Contains(source, StringComparer.Ordinal)) return false;
        closesWholeRegion = true;
        return true;
    }

    private bool SameScope(string left, string right) => scopes.TryGetValue(left, out var first) && scopes.TryGetValue(right, out var second) && first == second;

    private HashSet<string> Reach(string start)
    {
        var result = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(start);
        while (pending.TryPop(out var key) && edges.ContainsKey(key))
            if (result.Add(key)) foreach (var next in edges[key]) pending.Push(next);
        return result;
    }

    public static bool TryCreate(WorkflowDefinitionDraft draft, IReadOnlyDictionary<string, string[]> normalEdges, out WorkflowStructuredRegionPlan? plan)
    {
        plan = null;
        if (draft.Nodes is null || draft.Nodes.Count is < 2 or > WorkflowDefinitionCompiler.MaximumNodes ||
            draft.Nodes.Any(node => node is null || !WorkflowNodeKeyValidator.IsValid(node.NodeKey) || node.Config.ValueKind != System.Text.Json.JsonValueKind.Object) ||
            draft.Nodes.Select(node => node.NodeKey).Distinct(StringComparer.Ordinal).Count() != draft.Nodes.Count) return false;
        var nodes = draft.Nodes.ToDictionary(node => node.NodeKey, StringComparer.Ordinal);
        if (normalEdges.Count != nodes.Count || normalEdges.Values.Any(value => value is null || value.Length > WorkflowDefinitionCompiler.MaximumEdges) ||
            normalEdges.Values.Sum(value => (long)value.Length) > WorkflowDefinitionCompiler.MaximumEdges) return false;
        // 复制调用方集合，保证编译后引用与分支身份不会被后续编辑改变。
        var edges = normalEdges.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        if (edges.Count != nodes.Count || nodes.Keys.Any(key => !edges.ContainsKey(key)) ||
            edges.Values.Sum(value => value.Length) > WorkflowDefinitionCompiler.MaximumEdges ||
            edges.Values.Any(value => value.Distinct(StringComparer.Ordinal).Count() != value.Length) ||
            edges.Values.SelectMany(value => value).Any(key => !nodes.ContainsKey(key))) return false;
        var starts = draft.Nodes.Where(node => node.NodeTypeKey == "start").ToArray();
        if (starts is not [var start]) return false;
        var parents = nodes.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var pair in edges) foreach (var target in pair.Value) parents[target].Add(pair.Key);
        if (parents[start.NodeKey].Count != 0 || parents.Any(pair => pair.Key != start.NodeKey && pair.Value.Count == 0)) return false;
        var remaining = parents.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
        var ready = new Queue<string>([start.NodeKey]); var order = new List<string>();
        while (ready.TryDequeue(out var key))
        {
            order.Add(key);
            foreach (var next in edges[key]) if (--remaining[next] == 0) ready.Enqueue(next);
        }
        if (order.Count != nodes.Count || nodes.Values.Any(node => (node.NodeTypeKey == "end") != (edges[node.NodeKey].Length == 0))) return false;
        var dominators = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var postDominators = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        static HashSet<string> Intersect(IEnumerable<HashSet<string>> sources)
        {
            HashSet<string>? result = null;
            foreach (var source in sources) { if (result is null) result = new(source, StringComparer.Ordinal); else result.IntersectWith(source); }
            return result ?? new(StringComparer.Ordinal);
        }
        foreach (var key in order) { var set = Intersect(parents[key].Select(parent => dominators[parent])); set.Add(key); dominators.Add(key, set); }
        foreach (var key in order.AsEnumerable().Reverse()) { var set = Intersect(edges[key].Select(next => postDominators[next])); set.Add(key); postDominators.Add(key, set); }
        var scopes = new Dictionary<string, string>(StringComparer.Ordinal);
        var scopePaths = new Dictionary<string, IReadOnlyList<Frame>>(StringComparer.Ordinal);
        var forbiddenEntries = new HashSet<string>(StringComparer.Ordinal) { start.NodeKey };
        bool Visit(string key, IReadOnlyList<Frame> incoming)
        {
            var stack = incoming.ToList();
            var popped = new List<Frame>();
            while (stack.Count != 0 && stack[^1].JoinKey == key) { popped.Add(stack[^1]); stack.RemoveAt(stack.Count - 1); }
            var node = nodes[key];
            var explicitJoin = node.NodeTypeKey is "gateway.parallel" or "gateway.inclusive" && node.Config.TryGetProperty("gatewayRoleKey", out var role) &&
                role.ValueKind == System.Text.Json.JsonValueKind.String && role.GetString() == "join";
            if (explicitJoin && (!node.Config.TryGetProperty("forkNodeKey", out var forkKey) ||
                forkKey.ValueKind != System.Text.Json.JsonValueKind.String ||
                popped.Count(frame => frame.Explicit && frame.ForkKey == forkKey.GetString()) != 1)) return false;
            if (explicitJoin) forbiddenEntries.Add(key);
            if (node.NodeTypeKey == "end") { if (stack.Any(frame => frame.Explicit)) return false; stack.Clear(); }
            var signature = string.Join('\n', stack.Select(frame => frame.ForkKey + ":" + frame.BranchKey));
            if (scopes.TryGetValue(key, out var previous)) return previous == signature;
            scopes.Add(key, signature);
            scopePaths.Add(key, stack.ToArray());
            string? join = null; IReadOnlyList<(string BranchKey, string NextNodeKey)>? branches = null; var isExplicit = false;
            if (node.NodeTypeKey == "gateway.parallel")
            {
                if (!WorkflowParallelGatewayConfiguration.TryRead(node.Config, out var gateway)) return false;
                if (gateway!.Role == WorkflowParallelGatewayRole.Fork) { join = gateway.JoinNodeKey; branches = gateway.Branches.Select(branch => (branch.BranchKey, branch.NextNodeKey)).ToArray(); isExplicit = true; }
            }
            else if (node.NodeTypeKey == "gateway.inclusive")
            {
                if (!WorkflowInclusiveGatewayConfiguration.TryRead(node.Config, null, out var gateway)) return false;
                if (gateway!.Role == WorkflowInclusiveGatewayRole.Fork) { join = gateway.JoinNodeKey; branches = gateway.Branches.Select(branch => (branch.BranchKey, branch.NextNodeKey)).Append(("$default", gateway.DefaultNextNodeKey)).ToArray(); isExplicit = true; }
            }
            else if (node.NodeTypeKey == "gateway.exclusive")
            {
                if (!WorkflowExclusiveGatewayConfiguration.TryRead(node.Config, null, out var gateway)) return false;
                join = order.FirstOrDefault(candidate => candidate != key && postDominators[key].Contains(candidate));
                branches = gateway!.Branches.Select(branch => (branch.BranchKey, branch.NextNodeKey)).Append(("$default", gateway.DefaultNextNodeKey)).ToArray();
            }
            if (branches is null) return edges[key].All(next => Visit(next, stack));
            if (stack.Count >= 8 || isExplicit && (join is null || !postDominators[key].Contains(join))) return false;
            if (!branches.Select(branch => branch.NextNodeKey).ToHashSet(StringComparer.Ordinal).SetEquals(edges[key])) return false;
            return branches.All(branch => Visit(branch.NextNodeKey, [..stack, new Frame(key, branch.BranchKey, join, isExplicit)]));
        }
        if (!Visit(start.NodeKey, []) || scopes.Count != nodes.Count) return false;
        plan = new(edges, dominators, postDominators, scopes, forbiddenEntries, scopePaths); return true;
    }

    private sealed record Frame(string ForkKey, string BranchKey, string? JoinKey, bool Explicit);
}

/// <summary>固定区域保留原节点引用，不递归展开画布或生成另一份节点身份。</summary>
internal sealed record WorkflowStructuredRegion(string EntryNodeKey, string ExitNodeKey, IReadOnlyList<string> NodeKeys);
