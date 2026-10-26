using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>描述发布版本中固化的多人审批办理人和完成门槛。</summary>
/// <param name="ModeKey">稳定模式键：all、any、nOfM 或 percent。</param>
/// <param name="ApproverUserIds">去重后的可信办理人标识快照。</param>
/// <param name="RequiredApprovals">节点批准所需的同意票数。</param>
/// <param name="RequiredPercentage">比例模式的整数百分比；激活时按实际成员人数向上取整。</param>
internal sealed record WorkflowApprovalPolicy(
    string ModeKey,
    IReadOnlyList<Guid> ApproverUserIds,
    int RequiredApprovals,
    int? RequiredPercentage = null,
    WorkflowGroupApprovalConfiguration? GroupConfiguration = null,
    IReadOnlyList<WorkflowApprovalGroupAssignees>? Groups = null)
{
    private const int MaximumApproverCount = 20;

    /// <summary>门槛只按实际固定成员计算，不能沿用配置列表人数或把空集合视作通过。</summary>
    public int ResolveRequiredCount(int count) => count is < 1 or > MaximumApproverCount ? 0 : ModeKey switch
    {
        "single" when count == 1 => 1,
        "all" or "sequential" => count,
        "any" => 1,
        "percent" when RequiredPercentage is >= 1 and <= 100 => (count * RequiredPercentage.Value + 99) / 100,
        "nOfM" when RequiredApprovals > 1 && RequiredApprovals < count => RequiredApprovals,
        _ => 0,
    };

    /// <summary>从人工审批节点配置读取闭合的多人审批策略。</summary>
    /// <param name="config">人工审批节点配置。</param>
    /// <param name="policy">解析成功的策略；未配置时为空并保留旧单人语义。</param>
    /// <returns>配置缺失或结构、模式和票数全部有效时返回 <see langword="true"/>。</returns>
    public static bool TryRead(JsonElement config, out WorkflowApprovalPolicy? policy)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (config.TryGetProperty("groups", out _))
        {
            if (!WorkflowGroupApprovalConfiguration.TryRead(config, out var grouped)) return false;
            policy = new("group", [], grouped!.RequiredGroups, GroupConfiguration: grouped);
            return true;
        }

        if (!config.TryGetProperty("approvalPolicy", out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("modeKey", out var modeElement) ||
            modeElement.ValueKind != JsonValueKind.String ||
            !element.TryGetProperty("approverUserIds", out var usersElement) ||
            usersElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var modeKey = modeElement.GetString();
        var allowedPropertyCount = modeKey is "nOfM" or "percent" ? 3 : 2;
        if (element.EnumerateObject().Count() != allowedPropertyCount)
        {
            return false;
        }

        var users = new List<Guid>();
        foreach (var item in usersElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(item.GetString(), out var userId) ||
                userId == Guid.Empty ||
                users.Contains(userId))
            {
                return false;
            }

            users.Add(userId);
        }

        var hasAssigneeSources = config.TryGetProperty("assigneePolicy", out _) &&
            WorkflowAssigneePolicy.TryRead(config, out var sources) && sources.Sources.Count > 0;
        if (users.Count > MaximumApproverCount || users.Count < 2 && !hasAssigneeSources)
        {
            return false;
        }

        int? percentage = null;
        if (modeKey == "percent")
        {
            if (!element.TryGetProperty("requiredPercentage", out var percentageElement) ||
                percentageElement.ValueKind != JsonValueKind.Number ||
                !percentageElement.TryGetInt32(out var value) || value is < 1 or > 100)
                return false;
            percentage = value;
        }
        var required = modeKey switch
        {
            // 动态来源人数只在激活时确定；非零占位不能被当作已解析或自动通过。
            "all" or "sequential" => Math.Max(1, users.Count),
            "any" => 1,
            "percent" => Math.Max(1, (users.Count * percentage!.Value + 99) / 100),
            "nOfM" when element.TryGetProperty("requiredApprovals", out var requiredElement) &&
                requiredElement.ValueKind == JsonValueKind.Number &&
                requiredElement.TryGetInt32(out var value) &&
                value > 1 && value < (hasAssigneeSources ? MaximumApproverCount : users.Count) => value,
            _ => 0,
        };
        if (required == 0 ||
            modeKey != "nOfM" && element.TryGetProperty("requiredApprovals", out _))
        {
            return false;
        }

        policy = new WorkflowApprovalPolicy(modeKey!, users, required, percentage);
        return true;
    }
}
