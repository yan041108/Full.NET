namespace Full.NET.Modules.Workflow.Domain;

/// <summary>工作流待办/已办列表筛选与分页边界规则。</summary>
internal static class WorkflowTodoListRules
{
    private static readonly HashSet<string> ValidResultActionKeys = new(StringComparer.Ordinal)
    {
        "approve",
        "reject",
        "return",
        "cancel",
        "cancelled",
    };

    /// <summary>校验处理结果动作筛选键；空值表示不过滤。</summary>
    /// <param name="resultActionKey">待办完成时写入的结果动作键。</param>
    /// <returns>为空或属于受支持动作键时返回 <see langword="true"/>。</returns>
    public static bool IsValidResultActionKey(string? resultActionKey) =>
        resultActionKey is null || ValidResultActionKeys.Contains(resultActionKey);

    /// <summary>校验到达时间范围；任一端为空时视为开放区间。</summary>
    /// <param name="fromUtc">起始时间（UTC）。</param>
    /// <param name="toUtc">结束时间（UTC）。</param>
    /// <returns>范围合法或开放时返回 <see langword="true"/>。</returns>
    public static bool IsValidTimeRange(DateTimeOffset? fromUtc, DateTimeOffset? toUtc) =>
        fromUtc is null || toUtc is null || fromUtc <= toUtc;

    /// <summary>规范化定义键筛选；空白输入视为不过滤。</summary>
    /// <param name="definitionKey">流程定义键。</param>
    /// <returns>去首尾空白后的定义键，或 <see langword="null"/>。</returns>
    public static string? NormalizeDefinitionKey(string? definitionKey) =>
        string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey.Trim();

    /// <summary>规范化业务类型筛选；空白输入视为不过滤。</summary>
    /// <param name="businessType">稳定业务类型。</param>
    /// <returns>去首尾空白后的业务类型，或 <see langword="null"/>。</returns>
    public static string? NormalizeBusinessType(string? businessType) =>
        string.IsNullOrWhiteSpace(businessType) ? null : businessType.Trim();
}
