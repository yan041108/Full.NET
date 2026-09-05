using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>描述发布版本中固化的多人审批办理人和完成门槛。</summary>
/// <param name="ModeKey">稳定模式键：all、any 或 nOfM。</param>
/// <param name="ApproverUserIds">去重后的可信办理人标识快照。</param>
/// <param name="RequiredApprovals">节点批准所需的同意票数。</param>
internal sealed record WorkflowApprovalPolicy(
    string ModeKey,
    IReadOnlyList<Guid> ApproverUserIds,
    int RequiredApprovals)
{
    private const int MaximumApproverCount = 20;

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
        var allowedPropertyCount = modeKey == "nOfM" ? 3 : 2;
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

        if (users.Count is < 2 or > MaximumApproverCount)
        {
            return false;
        }

        var required = modeKey switch
        {
            "all" => users.Count,
            "any" => 1,
            "nOfM" when element.TryGetProperty("requiredApprovals", out var requiredElement) &&
                requiredElement.ValueKind == JsonValueKind.Number &&
                requiredElement.TryGetInt32(out var value) &&
                value > 1 && value < users.Count => value,
            _ => 0,
        };
        if (required == 0 ||
            modeKey != "nOfM" && element.TryGetProperty("requiredApprovals", out _))
        {
            return false;
        }

        policy = new WorkflowApprovalPolicy(modeKey!, users, required);
        return true;
    }
}
