using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>活动级截止独立于待办催办；系统通过不得增加个人票数或生成签名。</summary>
internal sealed record WorkflowSlaPolicy(int DueAfterMinutes, string PauseModeKey, string ActionKey)
{
    /// <summary>只允许显式暂停语义和静态处置；投票、办理及抢单不能被系统伪装为个人提交。</summary>
    public static bool TryRead(JsonElement config, string nodeTypeKey, out WorkflowSlaPolicy? policy)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object) return false;
        if (!config.TryGetProperty("slaPolicy", out var value)) return true;
        if (!WorkflowHumanActivityKinds.IsFormActivity(nodeTypeKey) || value.ValueKind != JsonValueKind.Object ||
            value.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count() ||
            value.EnumerateObject().Any(item => item.Name is not ("dueAfterMinutes" or "pauseModeKey" or "actionKey")) ||
            !value.TryGetProperty("dueAfterMinutes", out var due) || due.ValueKind != JsonValueKind.Number || !due.TryGetInt32(out var minutes) || minutes is < 1 or > 525600 ||
            !value.TryGetProperty("pauseModeKey", out var pause) || pause.ValueKind != JsonValueKind.String || pause.GetString() is not ("elapsed" or "freeze") ||
            !value.TryGetProperty("actionKey", out var action) || action.ValueKind != JsonValueKind.String || action.GetString() is not ("suspend" or "auto_reject" or "auto_pass") ||
            action.GetString() == "auto_pass" && nodeTypeKey is not ("human.approval" or "human.group_approval")) return false;
        policy = new(minutes, pause.GetString()!, action.GetString()!); return true;
    }

    /// <summary>冻结只保存尚未消耗的墙钟毫秒；向上取整避免恢复后提前到期。</summary>
    public static long RemainingMilliseconds(DateTimeOffset due, DateTimeOffset pausedAt) =>
        Math.Max(0, (long)Math.Ceiling((due - pausedAt).TotalMilliseconds));
}
