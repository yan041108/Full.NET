using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.DataApproval.Domain;

/// <summary>静态场景目录项：登记可配置场景及其作用域与 Workflow 业务类型。</summary>
/// <param name="ScenarioKey">稳定场景键。</param>
/// <param name="ScopeKey">允许配置的作用域键，当前仅支持 host。</param>
/// <param name="WorkflowBusinessType">启动工作流时使用的稳定业务类型机器码。</param>
public sealed record DataApprovalScenarioCatalogEntry(
    string ScenarioKey,
    string ScopeKey,
    string WorkflowBusinessType);

/// <summary>DataApproval 静态场景目录：未登记场景不得创建审批请求。</summary>
public static class DataApprovalScenarioCatalog
{
    private static readonly DataApprovalScenarioCatalogEntry[] Entries =
    [
        new(
            DataApprovalScenarioKeys.SerialRuleHostUpdate,
            "host",
            DataApprovalWorkflowBusinessTypes.SerialRuleUpdate),
    ];

    /// <summary>返回当前版本登记的全部静态场景。</summary>
    public static IReadOnlyList<DataApprovalScenarioCatalogEntry> All => Entries;

    /// <summary>按场景键查找目录项；未知键返回 null。</summary>
    /// <param name="scenarioKey">待查找场景键。</param>
    public static DataApprovalScenarioCatalogEntry? Find(string? scenarioKey)
    {
        var normalized = scenarioKey?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        foreach (var entry in Entries)
        {
            if (string.Equals(entry.ScenarioKey, normalized, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>判断场景键是否已登记且适用于指定作用域。</summary>
    /// <param name="scenarioKey">场景键。</param>
    /// <param name="scopeKey">当前可信作用域键。</param>
    public static bool IsRegisteredForScope(string? scenarioKey, string scopeKey)
    {
        var entry = Find(scenarioKey);
        return entry is not null &&
               string.Equals(entry.ScopeKey, scopeKey, StringComparison.Ordinal);
    }

    /// <summary>为工作流启动生成稳定业务标题快照。</summary>
    /// <param name="scenarioKey">场景键。</param>
    /// <param name="targetEntityId">被变更实体标识。</param>
    public static string FormatWorkflowBusinessTitle(string scenarioKey, Guid targetEntityId) =>
        $"{scenarioKey} · {targetEntityId:D}";
}
