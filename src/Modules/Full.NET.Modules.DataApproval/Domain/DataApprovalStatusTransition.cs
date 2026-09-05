using Full.NET.Modules.DataApproval.Contracts;

namespace Full.NET.Modules.DataApproval.Domain;

/// <summary>校验 DataApproval 场景键与状态转换规则。</summary>
public static class DataApprovalScenarioValidator
{
    /// <summary>判断场景键是否为当前切片已支持的场景。</summary>
    /// <param name="scenarioKey">待校验场景键。</param>
    public static bool IsSupportedScenario(string? scenarioKey) =>
        string.Equals(
            scenarioKey?.Trim(),
            DataApprovalScenarioKeys.SerialRuleHostUpdate,
            StringComparison.Ordinal);
}

/// <summary>DataApproval 请求状态机转换规则。</summary>
public static class DataApprovalStatusTransition
{
    /// <summary>判断请求是否仍可取消。</summary>
    /// <param name="statusKey">当前状态键。</param>
    public static bool CanCancel(string statusKey) =>
        string.Equals(statusKey, DataApprovalStatusKeys.Pending, StringComparison.Ordinal) ||
        string.Equals(statusKey, DataApprovalStatusKeys.InReview, StringComparison.Ordinal);

    /// <summary>判断工作流完成事件是否仍可驱动终态写入。</summary>
    /// <param name="statusKey">当前状态键。</param>
    public static bool CanResolveFromWorkflow(string statusKey) =>
        string.Equals(statusKey, DataApprovalStatusKeys.InReview, StringComparison.Ordinal);

    /// <summary>根据工作流实例终态解析 DataApproval 目标状态。</summary>
    /// <param name="workflowStatusKey">工作流实例状态键。</param>
    public static string? MapWorkflowTerminalStatus(string workflowStatusKey) =>
        workflowStatusKey switch
        {
            "completed" => DataApprovalStatusKeys.Approved,
            "rejected" => DataApprovalStatusKeys.Rejected,
            "cancelled" => DataApprovalStatusKeys.Cancelled,
            _ => null,
        };
}
