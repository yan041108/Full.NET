using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>描述当前执行器已经闭合的线性审批计划，防止定义校验与运行时推进规则漂移。</summary>
internal sealed class WorkflowRuntimePlan
{
    private readonly IReadOnlyList<string> approvalNodeKeys;
    private readonly IReadOnlyDictionary<string, int> approvalIndexes;

    private WorkflowRuntimePlan(IReadOnlyList<string> approvalNodeKeys)
    {
        this.approvalNodeKeys = approvalNodeKeys;
        approvalIndexes = approvalNodeKeys
            .Select((nodeKey, index) => (nodeKey, index))
            .ToDictionary(item => item.nodeKey, item => item.index, StringComparer.Ordinal);
    }

    public string FirstApprovalNodeKey => approvalNodeKeys[0];

    public static bool TryCreate(WorkflowDefinitionDraft draft, out WorkflowRuntimePlan? plan)
    {
        plan = null;
        if (draft.Nodes.Count < 3 ||
            draft.Nodes.GroupBy(node => node.NodeKey, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return false;
        }

        var nodes = draft.Nodes.ToDictionary(node => node.NodeKey, StringComparer.Ordinal);
        var start = draft.Nodes.SingleOrDefault(node => node.NodeTypeKey == "start");
        if (start is null || !TryReadSingleNext(start.Config, out var current))
        {
            return false;
        }

        var approvals = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.NodeKey };
        while (current is not null && visited.Add(current) && nodes.TryGetValue(current, out var node))
        {
            if (node.NodeTypeKey == "end")
            {
                if (!HasNoNext(node.Config) || approvals.Count == 0 || visited.Count != nodes.Count)
                {
                    return false;
                }

                plan = new WorkflowRuntimePlan(approvals);
                return true;
            }

            if (node.NodeTypeKey != "human.approval" || !TryReadSingleNext(node.Config, out current))
            {
                return false;
            }

            approvals.Add(node.NodeKey);
        }

        return false;
    }

    public bool TryResolveApproval(string nodeKey, out WorkflowApprovalTransition transition)
    {
        if (!approvalIndexes.TryGetValue(nodeKey, out var index))
        {
            transition = default;
            return false;
        }

        var completes = index == approvalNodeKeys.Count - 1;
        transition = new WorkflowApprovalTransition(
            completes ? null : approvalNodeKeys[index + 1],
            completes);
        return true;
    }

    private static bool TryReadSingleNext(JsonElement config, out string? nextNodeKey)
    {
        nextNodeKey = null;
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("nextNodeKeys", out var keys) ||
            keys.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = keys.EnumerateArray().ToArray();
        if (values is not [var value] || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        nextNodeKey = value.GetString();
        return !string.IsNullOrWhiteSpace(nextNodeKey);
    }

    private static bool HasNoNext(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return !config.TryGetProperty("nextNodeKeys", out var keys) ||
               keys.ValueKind == JsonValueKind.Array && !keys.EnumerateArray().Any();
    }
}

internal readonly record struct WorkflowApprovalTransition(
    string? NextApprovalNodeKey,
    bool CompletesInstance);
