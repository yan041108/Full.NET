using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>分组定义分别固定组内策略和组间门槛；成员仍须在激活时经所有者目录解析。</summary>
internal sealed record WorkflowGroupApprovalConfiguration(string ModeKey, int RequiredGroups,
    int? RequiredPercentage, IReadOnlyList<WorkflowApprovalGroupDefinition> Groups)
{
    public static bool TryRead(JsonElement config, out WorkflowGroupApprovalConfiguration? result)
    {
        result = null;
        if (config.ValueKind != JsonValueKind.Object || config.TryGetProperty("assigneePolicy", out _) ||
            config.EnumerateObject().GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1) ||
            !config.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() is < 2 or > 8 ||
            !config.TryGetProperty("groupApprovalPolicy", out var gate) || gate.ValueKind != JsonValueKind.Object ||
            !gate.TryGetProperty("modeKey", out var mode) || mode.ValueKind != JsonValueKind.String)
            return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new List<WorkflowApprovalGroupDefinition>();
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object ||
                group.EnumerateObject().GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1) ||
                group.EnumerateObject().Any(x => x.Name is not ("groupKey" or "groupName" or "assigneePolicy" or "approvalPolicy")) ||
                !group.TryGetProperty("groupKey", out var keyElement) || keyElement.ValueKind != JsonValueKind.String ||
                keyElement.GetString() is not { } key || !IsValidKey(key) || !keys.Add(key) ||
                (!group.TryGetProperty("assigneePolicy", out _) && !group.TryGetProperty("approvalPolicy", out _)) ||
                !WorkflowAssigneePolicy.TryRead(group, out var assignees) || !WorkflowApprovalPolicy.TryRead(group, out var approvals))
                return false;
            string? name = null;
            if (group.TryGetProperty("groupName", out var nameElement))
            {
                if (nameElement.ValueKind != JsonValueKind.String || nameElement.GetString()?.Trim() is not { Length: >= 1 and <= 128 } value)
                    return false;
                name = value;
            }
            definitions.Add(new(key, name, assignees, approvals));
        }
        var modeKey = mode.GetString();
        var expectedCount = modeKey is "nOfM" or "percent" ? 2 : 1;
        if (gate.EnumerateObject().Count() != expectedCount) return false;
        int? percentage = null;
        var required = modeKey switch
        {
            "all" => definitions.Count,
            "any" => 1,
            "nOfM" when gate.TryGetProperty("requiredApprovals", out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var n) && n > 1 && n < definitions.Count => n,
            "percent" when gate.TryGetProperty("requiredPercentage", out var rate) && rate.ValueKind == JsonValueKind.Number && rate.TryGetInt32(out var p) && p is >= 1 and <= 100 => SetPercentage(p),
            _ => 0,
        };
        if (required == 0) return false;
        result = new(modeKey!, required, percentage, definitions);
        return true;

        int SetPercentage(int value) { percentage = value; return (definitions.Count * value + 99) / 100; }
    }

    private static bool IsValidKey(string value) => value.Length is >= 1 and <= 64 && char.IsAsciiLetterLower(value[0]) &&
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
}

/// <summary>组键及组内来源/门槛只来自不可变发布版本，不接受办理请求重定义。</summary>
internal sealed record WorkflowApprovalGroupDefinition(string GroupKey, string? GroupName,
    WorkflowAssigneePolicy AssigneePolicy, WorkflowApprovalPolicy? ApprovalPolicy);

/// <summary>组内实际去重成员与按实际分母计算的门槛，等待写入同一活动步骤。</summary>
internal sealed record WorkflowApprovalGroupAssignees(string GroupKey, string? GroupName, WorkflowApprovalPolicy Policy,
    WorkflowAssigneeHandlingDisposition? Handling = null);
