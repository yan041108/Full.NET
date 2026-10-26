using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>撤回边界由发布版本的开始节点固定；缺省关闭，不能借管理员取消权限放开发起人撤回。</summary>
internal sealed record WorkflowWithdrawalPolicy(string ModeKey, IReadOnlySet<string> NodeKeys)
{
    public static bool TryRead(JsonElement startConfig, out WorkflowWithdrawalPolicy? policy)
    {
        policy = null;
        if (startConfig.ValueKind != JsonValueKind.Object || startConfig.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1)) return false;
        if (!startConfig.TryGetProperty("withdrawalPolicy", out var value)) return true;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            !value.TryGetProperty("modeKey", out var mode) || mode.ValueKind != JsonValueKind.String) return false;
        var key = mode.GetString();
        if (key is "disabled" or "before_any_human_action")
        {
            if (value.EnumerateObject().Count() != 1) return false;
            policy = new(key, new HashSet<string>(StringComparer.Ordinal));
            return true;
        }
        if (key != "at_active_nodes" || value.EnumerateObject().Count() != 2 ||
            !value.TryGetProperty("nodeKeys", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() is < 1 or > 16) return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes.EnumerateArray())
            if (node.ValueKind != JsonValueKind.String || !WorkflowNodeKeyValidator.IsValid(node.GetString()) || !keys.Add(node.GetString()!)) return false;
        policy = new(key, keys); return true;
    }

    /// <summary>未知活动默认有外部副作用风险；以后增加节点不能静默扩大现有撤回授权。</summary>
    public bool Allows(IReadOnlyList<WorkflowWithdrawalActivityBoundary> reached, bool hasHumanAction)
    {
        if (reached.Any(activity => !IsSideEffectFree(activity.NodeTypeKey))) return false;
        if (ModeKey == "before_any_human_action") return !hasHumanAction;
        if (ModeKey != "at_active_nodes") return false;
        var active = reached.Where(activity => activity.StatusKey is "active" or "waiting").ToArray();
        return active.Length > 0 && active.All(activity => WorkflowHumanActivityKinds.IsFormActivity(activity.NodeTypeKey) && NodeKeys.Contains(activity.NodeKey));
    }

    private static bool IsSideEffectFree(string kind) =>
        // 抄送是已发生的知会，撤回不伪装成撤销历史通知；业务副作用节点必须另走补偿协议。
        kind is "human.approval" or "human.group_approval" or "human.task" or "human.vote" or "human.claim" or
            "start" or "end" or "control.empty" or "notify.cc" or "gateway.exclusive" or "gateway.parallel" or "gateway.inclusive";
}

/// <summary>来自同一实例的实际到达事实，不用尚未执行的设计图代替外部副作用判断。</summary>
internal sealed record WorkflowWithdrawalActivityBoundary(string NodeKey, string NodeTypeKey, string StatusKey);
