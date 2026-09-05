namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>可信业务类型对应的 Admin 路由导航描述。</summary>
/// <param name="RouteName">Vue Router 路由名。</param>
/// <param name="IdQueryKey">业务标识对应的 query 键。</param>
public sealed record WorkflowBusinessDetailRoute(
    string RouteName,
    string IdQueryKey);

/// <summary>
/// 工作流业务详情导航白名单；仅允许跳转到已登记的后台路由，
/// 禁止接受任意 URL 或动态拼接外部链接。
/// </summary>
public static class WorkflowBusinessDetailRoutes
{
    private static readonly IReadOnlyDictionary<string, WorkflowBusinessDetailRoute> Routes =
        new Dictionary<string, WorkflowBusinessDetailRoute>(StringComparer.Ordinal)
        {
            [DataApprovalWorkflowBusinessTypes.SerialRuleUpdate] = new(
                "data-approval-requests",
                "requestId"),
        };

    /// <summary>按稳定业务类型查找可信详情导航。</summary>
    /// <param name="businessType">业务类型机器码。</param>
    /// <returns>已登记时返回路由描述，否则返回 <see langword="null"/>。</returns>
    public static WorkflowBusinessDetailRoute? Find(string? businessType)
    {
        var normalized = businessType?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return Routes.TryGetValue(normalized, out var route) ? route : null;
    }
}
