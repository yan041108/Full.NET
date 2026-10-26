using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>办理使用明确完成规则，不把提交解释为审批同意，也不满足审批类人审屏障。</summary>
internal sealed record WorkflowTaskPolicy(string CompletionModeKey)
{
    public static bool TryRead(JsonElement config, out WorkflowTaskPolicy? policy)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object ||
            config.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            !config.TryGetProperty("taskPolicy", out var element) || element.ValueKind != JsonValueKind.Object ||
            element.EnumerateObject().Count() != 1 || !element.TryGetProperty("completionModeKey", out var mode) ||
            mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("all" or "any" or "sequential")) return false;
        policy = new(mode.GetString()!);
        return true;
    }

    public int RequiredCompletions(int memberCount) => memberCount is < 1 or > 20 ? 0 : CompletionModeKey switch
    {
        "any" => 1, "all" or "sequential" => memberCount, _ => 0,
    };
}

/// <summary>一次激活冻结实际办理人顺序，后续提交不重新解析或改变分母。</summary>
internal sealed record WorkflowTaskActivationPolicy(WorkflowTaskPolicy Policy, IReadOnlyList<Guid> MemberUserIds);
