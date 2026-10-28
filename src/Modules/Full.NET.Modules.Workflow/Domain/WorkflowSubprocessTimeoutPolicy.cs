using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>子流程截止只选择已发布路径；收敛前不得把取消请求当作子级外部效果已确认。</summary>
internal sealed record WorkflowSubprocessTimeoutPolicy(int DueAfterMinutes, string PauseModeKey, string ActionKey, string? NextNodeKey)
{
    /// <summary>当前静态跳转只跳过同一既有路径的前段；独立分支和回边留给带执行代次的路由协议。</summary>
    public static bool ValidateTargets(IReadOnlyDictionary<string, WorkflowNodeDraft> nodes, IReadOnlyDictionary<string, string[]> outgoing)
    {
        foreach (var node in nodes.Values.Where(node => node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType))
        {
            if (!TryRead(node.Config, out var policy)) return false;
            if (policy?.NextNodeKey is not { } target) continue;
            if (!node.Config.TryGetProperty("nextNodeKeys", out var next) || next.ValueKind != JsonValueKind.Array || next.GetArrayLength() != 1 || next[0].ValueKind != JsonValueKind.String) return false;
            var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(next[0].GetString()!); var found = false;
            while (pending.TryPop(out var key))
            {
                if (!visited.Add(key)) continue;
                if (key == target) { found = true; break; }
                if (!nodes.TryGetValue(key, out var current) || !outgoing.TryGetValue(key, out var edges)) return false;
                if (current.NodeTypeKey == WorkflowSubprocessPolicy.NodeType && current.Config.TryGetProperty("nextNodeKeys", out var normal) && normal.ValueKind == JsonValueKind.Array)
                    edges = normal.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray();
                foreach (var edge in edges) pending.Push(edge);
            }
            if (!found) return false;
        }
        return true;
    }

    public static bool TryRead(JsonElement config, out WorkflowSubprocessTimeoutPolicy? policy)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object) return false;
        if (!config.TryGetProperty("childTimeoutPolicy", out var value)) return true;
        if (!WorkflowSubprocessPolicy.Unique(value) || value.EnumerateObject().Any(item => item.Name is not
                ("dueAfterMinutes" or "pauseModeKey" or "actionKey" or "nextNodeKey")) ||
            !value.TryGetProperty("dueAfterMinutes", out var due) || due.ValueKind != JsonValueKind.Number || !due.TryGetInt32(out var minutes) || minutes is < 1 or > 525600 ||
            !value.TryGetProperty("pauseModeKey", out var pause) || pause.ValueKind != JsonValueKind.String || pause.GetString() is not ("freeze" or "elapsed") ||
            !value.TryGetProperty("actionKey", out var action) || action.ValueKind != JsonValueKind.String || action.GetString() is not ("continue" or "route")) return false;
        string? next = null;
        var hasNext = value.TryGetProperty("nextNodeKey", out var target);
        if (action.GetString() == "route")
        {
            if (!hasNext || target.ValueKind != JsonValueKind.String || !WorkflowNodeKeyValidator.IsValid(next = target.GetString())) return false;
        }
        else if (hasNext) return false;
        policy = new(minutes, pause.GetString()!, action.GetString()!, next); return true;
    }
}
