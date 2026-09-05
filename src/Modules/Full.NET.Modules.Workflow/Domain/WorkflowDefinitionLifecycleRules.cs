namespace Full.NET.Modules.Workflow.Domain;

/// <summary>工作流定义启停、归档与版本删除边界规则。</summary>
internal static class WorkflowDefinitionLifecycleRules
{
    private static readonly HashSet<string> KnownStatusKeys = new(StringComparer.Ordinal)
    {
        WorkflowDefinitionStatusKeys.Active,
        WorkflowDefinitionStatusKeys.Disabled,
        WorkflowDefinitionStatusKeys.Archived,
    };

    /// <summary>校验状态键是否属于受支持集合。</summary>
    /// <param name="statusKey">待校验状态键。</param>
    /// <returns>属于受支持集合时返回 <see langword="true"/>。</returns>
    public static bool IsKnownStatusKey(string? statusKey) =>
        statusKey is not null && KnownStatusKeys.Contains(statusKey);

    /// <summary>校验从当前状态切换到目标状态是否允许。</summary>
    /// <param name="currentStatusKey">当前状态键。</param>
    /// <param name="targetStatusKey">目标状态键。</param>
    /// <returns>允许切换时返回 <see langword="true"/>。</returns>
    public static bool CanTransition(string currentStatusKey, string targetStatusKey)
    {
        if (!IsKnownStatusKey(currentStatusKey) || !IsKnownStatusKey(targetStatusKey))
        {
            return false;
        }

        if (currentStatusKey == targetStatusKey)
        {
            return true;
        }

        return currentStatusKey switch
        {
            WorkflowDefinitionStatusKeys.Active => targetStatusKey is WorkflowDefinitionStatusKeys.Disabled
                or WorkflowDefinitionStatusKeys.Archived,
            WorkflowDefinitionStatusKeys.Disabled => targetStatusKey is WorkflowDefinitionStatusKeys.Active
                or WorkflowDefinitionStatusKeys.Archived,
            _ => false,
        };
    }

    /// <summary>当前状态是否允许启动新实例。</summary>
    /// <param name="statusKey">定义状态键。</param>
    /// <returns>仅 active 返回 <see langword="true"/>。</returns>
    public static bool AllowsNewInstance(string statusKey) =>
        statusKey == WorkflowDefinitionStatusKeys.Active;

    /// <summary>当前状态是否允许发布新版本。</summary>
    /// <param name="statusKey">定义状态键。</param>
    /// <returns>仅 active 返回 <see langword="true"/>。</returns>
    public static bool AllowsPublish(string statusKey) =>
        statusKey == WorkflowDefinitionStatusKeys.Active;

    /// <summary>当前状态是否允许修改草稿。</summary>
    /// <param name="statusKey">定义状态键。</param>
    /// <returns>active 与 disabled 返回 <see langword="true"/>。</returns>
    public static bool AllowsDraftMutation(string statusKey) =>
        statusKey is WorkflowDefinitionStatusKeys.Active or WorkflowDefinitionStatusKeys.Disabled;
}
