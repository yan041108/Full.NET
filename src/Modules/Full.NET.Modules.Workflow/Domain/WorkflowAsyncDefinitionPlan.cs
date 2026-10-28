using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>从固定图编译异步结果的显式消费依赖；结构边界隐式等待，不在运行时猜测最新同名激活。</summary>
internal static class WorkflowAsyncDefinitionPlan
{
    internal const int MaximumDeclaredDependencies = 16;

    public static bool TryCompile(WorkflowDefinitionDraft draft, IReadOnlyDictionary<string, string[]> edges,
        out IReadOnlyDictionary<string, WorkflowAsyncWaitDefinition> waits)
    {
        var result = new Dictionary<string, WorkflowAsyncWaitDefinition>(StringComparer.Ordinal); waits = result;
        if (draft.Nodes is null || draft.Nodes.Count > WorkflowDefinitionCompiler.MaximumNodes ||
            draft.Nodes.Select(node => node.NodeKey).Distinct(StringComparer.Ordinal).Count() != draft.Nodes.Count) return false;
        var nodes = draft.Nodes.ToDictionary(node => node.NodeKey, StringComparer.Ordinal);
        if (edges.Count != nodes.Count || nodes.Keys.Any(key => !edges.ContainsKey(key)) || edges.Values.SelectMany(value => value).Any(key => !nodes.ContainsKey(key))) return false;
        var producers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var node in nodes.Values.Where(node => node.NodeTypeKey == "integration.async_trigger"))
        {
            if (!WorkflowConnectorPolicy.TryRead(node.Config, null, out var policy)) return false;
            producers.Add(node.NodeKey, policy!.OutputFields.Keys.ToHashSet(StringComparer.Ordinal));
        }
        var candidates = producers.Values.SelectMany(value => value).ToHashSet(StringComparer.Ordinal);
        var parents = nodes.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var pair in edges) foreach (var target in pair.Value) parents[target].Add(pair.Key);
        var remaining = parents.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
        var ready = new Queue<string>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var states = new Dictionary<string, FlowState>(StringComparer.Ordinal);
        while (ready.TryDequeue(out var key))
        {
            var node = nodes[key]; var incoming = parents[key].Select(parent => states[parent]).ToArray();
            var pending = incoming.SelectMany(state => state.Pending).ToHashSet(StringComparer.Ordinal);
            var guaranteed = incoming.Length == 0 ? new HashSet<string>(StringComparer.Ordinal) : incoming[0].Guaranteed.ToHashSet(StringComparer.Ordinal);
            foreach (var state in incoming.Skip(1)) guaranteed.IntersectWith(state.Guaranteed);
            var declared = node.AwaitActivityNodeKeys ?? [];
            if (declared.Count > MaximumDeclaredDependencies || declared.Distinct(StringComparer.Ordinal).Count() != declared.Count ||
                declared.Any(source => !WorkflowNodeKeyValidator.IsValid(source) || source == key || !producers.ContainsKey(source) || !guaranteed.Contains(source))) return false;
            var implicitBoundary = node.NodeTypeKey is "end" or "gateway.parallel" or "gateway.inclusive" or "business.command" or WorkflowSubprocessPolicy.NodeType or WorkflowDynamicRoutePolicy.NodeType;
            if (!TryReadConsumedFields(node, candidates, out var consumed)) return false;
            var required = pending.Where(source => producers[source].Overlaps(consumed)).ToArray();
            if (!implicitBoundary && required.Any(source => !declared.Contains(source, StringComparer.Ordinal))) return false;
            var awaited = (implicitBoundary ? pending : pending.Where(source => declared.Contains(source, StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal).ToArray();
            if (awaited.Length != 0) result.Add(key, new(key, Array.AsReadOnly(awaited), implicitBoundary));
            pending.ExceptWith(awaited);
            if (producers.ContainsKey(key)) { pending.Add(key); guaranteed.Add(key); }
            states.Add(key, new(pending, guaranteed));
            foreach (var next in edges[key]) if (--remaining[next] == 0) ready.Enqueue(next);
        }
        return states.Count == nodes.Count;
    }

    private static bool TryReadConsumedFields(WorkflowNodeDraft node, IReadOnlySet<string> candidates, out HashSet<string> fields)
    {
        fields = new(StringComparer.Ordinal);
        if (node.Config.ValueKind != JsonValueKind.Object) return false;
        if (WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey))
        {
            node.Config.TryGetProperty("fieldPolicies", out var access);
            foreach (var field in candidates)
                if (access.ValueKind != JsonValueKind.Object || !access.TryGetProperty(field, out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() != "hidden") fields.Add(field);
            if (node.Config.TryGetProperty("assigneePolicy", out var assignee)) ReadSourceFields(assignee, fields);
            if (node.Config.TryGetProperty("groups", out var groups)) ReadSourceFields(groups, fields);
            if (node.Config.TryGetProperty("votePolicy", out var vote) && vote.TryGetProperty("resultFieldKey", out var target) && target.ValueKind == JsonValueKind.String) fields.Add(target.GetString()!);
            return true;
        }
        switch (node.NodeTypeKey)
        {
            // 进入路由前所有实际异步激活均已收敛，不能在换代后采纳旧回执。
            case WorkflowDynamicRoutePolicy.NodeType: return WorkflowDynamicRoutePolicy.TryRead(node.Config, null, out _);
            case WorkflowSubprocessPolicy.NodeType:
                if (!WorkflowSubprocessPolicy.TryRead(node.Config, null, out var subprocess)) return false;
                fields.UnionWith(subprocess.ReadFields); fields.UnionWith(subprocess.WrittenFields); return true;
            case "start": case "end": case "control.empty": case "gateway.parallel": case "business.command": return true;
            case "integration.trigger": case "integration.async_trigger":
                if (!WorkflowConnectorPolicy.TryRead(node.Config, null, out var connector)) return false;
                fields.UnionWith(connector!.InputFields.Values); fields.UnionWith(connector.OutputFields.Keys); return true;
            case "timer.delay":
                if (node.Config.TryGetProperty("delayPolicy", out var delay) && delay.TryGetProperty("fieldKey", out var delayedField) && delayedField.ValueKind == JsonValueKind.String) fields.Add(delayedField.GetString()!);
                return true;
            case "notify.cc":
                if (node.Config.TryGetProperty("recipientPolicy", out var recipients)) ReadSourceFields(recipients, fields);
                return true;
            case "data.modify": case "data.clear":
                if (!WorkflowFieldMutationPolicy.TryRead(node.NodeTypeKey, node.Config, null, out var mutation)) return false;
                fields.UnionWith(mutation!.WrittenFields);
                var policy = node.Config.GetProperty("mutationPolicy");
                if (policy.TryGetProperty("assignments", out var assignments))
                    foreach (var assignment in assignments.EnumerateArray()) if (assignment.TryGetProperty("sourceFieldKey", out var source)) fields.Add(source.GetString()!);
                if (policy.TryGetProperty("condition", out var condition)) ReadConditionFields(condition, fields);
                return true;
            case "gateway.exclusive": case "gateway.inclusive":
                if (node.Config.TryGetProperty("branches", out var branches))
                    foreach (var branch in branches.EnumerateArray()) if (branch.TryGetProperty("condition", out var branchCondition)) ReadConditionFields(branchCondition, fields);
                return true;
            default: return false;
        }
    }

    private static void ReadConditionFields(JsonElement condition, ISet<string> fields)
    {
        if (condition.ValueKind != JsonValueKind.Object) return;
        if (condition.TryGetProperty("fieldKey", out var field) && field.ValueKind == JsonValueKind.String) fields.Add(field.GetString()!);
        if (condition.TryGetProperty("conditions", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray()) ReadConditionFields(child, fields);
    }

    private static void ReadSourceFields(JsonElement source, ISet<string> fields)
    {
        if (source.ValueKind == JsonValueKind.Array) { foreach (var child in source.EnumerateArray()) ReadSourceFields(child, fields); return; }
        if (source.ValueKind != JsonValueKind.Object) return;
        foreach (var property in source.EnumerateObject())
        {
            if (property.Name == "fieldKey" && property.Value.ValueKind == JsonValueKind.String) fields.Add(property.Value.GetString()!);
            else if (property.Name != "value") ReadSourceFields(property.Value, fields);
        }
    }

    private sealed record FlowState(HashSet<string> Pending, HashSet<string> Guaranteed);
}

/// <summary>隐式边界只绑定实际已激活的原CallId；显式消费者的依赖缺失必须拒绝恢复。</summary>
internal sealed record WorkflowAsyncWaitDefinition(string NodeKey, IReadOnlyList<string> ProducerNodeKeys, bool IsImplicitBoundary);
