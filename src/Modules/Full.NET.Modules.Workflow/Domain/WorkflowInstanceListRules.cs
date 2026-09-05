namespace Full.NET.Modules.Workflow.Domain;

/// <summary>工作流实例列表筛选与分页边界规则。</summary>
internal static class WorkflowInstanceListRules
{
    private static readonly HashSet<string> ValidStatusKeys = new(StringComparer.Ordinal)
    {
        "active",
        "completed",
        "rejected",
        "cancelled",
        "suspended",
    };

    /// <summary>校验状态筛选键；空值表示不过滤。</summary>
    /// <param name="statusKey">实例状态键。</param>
    /// <returns>为空或属于受支持终态/运行态时返回 <see langword="true"/>。</returns>
    public static bool IsValidStatusKey(string? statusKey) =>
        statusKey is null || ValidStatusKeys.Contains(statusKey);

    /// <summary>校验发起时间范围；任一端为空时视为开放区间。</summary>
    /// <param name="startedFromUtc">起始时间（UTC）。</param>
    /// <param name="startedToUtc">结束时间（UTC）。</param>
    /// <returns>范围合法或开放时返回 <see langword="true"/>。</returns>
    public static bool IsValidTimeRange(DateTimeOffset? startedFromUtc, DateTimeOffset? startedToUtc) =>
        startedFromUtc is null || startedToUtc is null || startedFromUtc <= startedToUtc;

    /// <summary>规范化定义键筛选；空白输入视为不过滤。</summary>
    /// <param name="definitionKey">流程定义键。</param>
    /// <returns>去首尾空白后的定义键，或 <see langword="null"/>。</returns>
    public static string? NormalizeDefinitionKey(string? definitionKey) =>
        string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey.Trim();
}
