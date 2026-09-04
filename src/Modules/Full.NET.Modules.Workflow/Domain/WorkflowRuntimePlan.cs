using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>描述执行器已经闭合的线性审批与抄送计划，防止定义校验和运行时推进规则漂移。</summary>
internal sealed class WorkflowRuntimePlan
{
    private readonly WorkflowApprovalTransition startTransition;
    private readonly IReadOnlyDictionary<string, WorkflowApprovalTransition> approvalTransitions;

    /// <summary>使用已经解析的启动迁移和审批迁移创建不可变运行计划。</summary>
    /// <param name="startTransition">从开始节点进入首个人工审批前需要执行的迁移。</param>
    /// <param name="approvalTransitions">每个人工审批通过后需要执行的迁移。</param>
    private WorkflowRuntimePlan(
        WorkflowApprovalTransition startTransition,
        IReadOnlyDictionary<string, WorkflowApprovalTransition> approvalTransitions)
    {
        this.startTransition = startTransition;
        this.approvalTransitions = approvalTransitions;
    }

    /// <summary>获取首个人工审批节点键；线性计划必须至少包含一个审批节点。</summary>
    public string FirstApprovalNodeKey => startTransition.NextApprovalNodeKey!;

    /// <summary>从定义草稿构造仅包含人工审批、抄送和终点的线性计划。</summary>
    /// <param name="draft">已经过结构反序列化的定义草稿。</param>
    /// <param name="plan">构造成功后的不可变运行计划。</param>
    /// <returns>拓扑和抄送配置均可由当前执行器闭合处理时返回 <see langword="true"/>。</returns>
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

        var visited = new HashSet<string>(StringComparer.Ordinal) { start.NodeKey };
        var pendingCc = new List<WorkflowCcRuntimeNode>();
        var approvals = new List<string>();
        var transitions = new Dictionary<string, WorkflowApprovalTransition>(StringComparer.Ordinal);
        string? previousApproval = null;
        WorkflowApprovalTransition? initial = null;

        while (current is not null && visited.Add(current) && nodes.TryGetValue(current, out var node))
        {
            if (node.NodeTypeKey == "end")
            {
                if (!HasNoNext(node.Config) || approvals.Count == 0 || visited.Count != nodes.Count)
                {
                    return false;
                }

                var terminal = new WorkflowApprovalTransition(
                    null,
                    true,
                    pendingCc.ToArray());
                if (previousApproval is null)
                {
                    return false;
                }

                transitions.Add(previousApproval, terminal);
                plan = new WorkflowRuntimePlan(initial!.Value, transitions);
                return true;
            }

            if (!TryReadSingleNext(node.Config, out var next))
            {
                return false;
            }

            if (node.NodeTypeKey == "notify.cc")
            {
                if (!WorkflowCcNodeConfiguration.TryReadRecipients(node.Config, out var recipients))
                {
                    return false;
                }

                pendingCc.Add(new WorkflowCcRuntimeNode(node.NodeKey, recipients));
                current = next;
                continue;
            }

            if (node.NodeTypeKey != "human.approval")
            {
                return false;
            }

            var transition = new WorkflowApprovalTransition(
                node.NodeKey,
                false,
                pendingCc.ToArray());
            if (previousApproval is null)
            {
                initial = transition;
            }
            else
            {
                transitions.Add(previousApproval, transition);
            }

            approvals.Add(node.NodeKey);
            previousApproval = node.NodeKey;
            pendingCc.Clear();
            current = next;
        }

        return false;
    }

    /// <summary>解析实例启动后、首个人工审批前需要执行的抄送节点。</summary>
    /// <param name="transition">启动迁移。</param>
    /// <returns>计划存在时始终返回 <see langword="true"/>。</returns>
    public bool TryResolveStart(out WorkflowApprovalTransition transition)
    {
        transition = startTransition;
        return true;
    }

    /// <summary>解析指定人工审批通过后的抄送、下一审批或结束迁移。</summary>
    /// <param name="nodeKey">当前人工审批节点键。</param>
    /// <param name="transition">匹配到的闭合迁移。</param>
    /// <returns>节点属于当前计划时返回 <see langword="true"/>。</returns>
    public bool TryResolveApproval(string nodeKey, out WorkflowApprovalTransition transition) =>
        approvalTransitions.TryGetValue(nodeKey, out transition);

    /// <summary>从节点配置读取唯一后继节点键。</summary>
    /// <param name="config">节点配置 JSON。</param>
    /// <param name="nextNodeKey">唯一后继节点键。</param>
    /// <returns>配置恰好包含一个非空字符串后继时返回 <see langword="true"/>。</returns>
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

    /// <summary>确认终点配置没有后继节点。</summary>
    /// <param name="config">终点配置 JSON。</param>
    /// <returns>后继字段缺失或为空数组时返回 <see langword="true"/>。</returns>
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

/// <summary>描述一次审批边界之间需要同步落库的抄送节点。</summary>
/// <param name="NodeKey">稳定抄送节点键。</param>
/// <param name="RecipientUserIds">经过编译校验的收件人用户标识。</param>
internal sealed record WorkflowCcRuntimeNode(
    string NodeKey,
    IReadOnlyList<Guid> RecipientUserIds);

/// <summary>描述启动或审批通过后到下一等待点的闭合迁移。</summary>
/// <param name="NextApprovalNodeKey">下一人工审批节点；流程结束时为空。</param>
/// <param name="CompletesInstance">迁移完成后是否结束实例。</param>
/// <param name="CcNodes">到达下一等待点前按顺序执行的抄送节点。</param>
internal readonly record struct WorkflowApprovalTransition(
    string? NextApprovalNodeKey,
    bool CompletesInstance,
    IReadOnlyList<WorkflowCcRuntimeNode> CcNodes);
