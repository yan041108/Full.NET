using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>解析并行网关分叉与汇合节点的闭合配置。</summary>
internal static class WorkflowParallelGatewayConfiguration
{
    private const int MinimumBranchCount = 2;
    private const int MaximumBranchCount = 8;
    private const int MaximumKeyLength = 128;

    /// <summary>读取并行网关节点配置。</summary>
    /// <param name="config">并行网关节点配置。</param>
    /// <param name="definition">成功解析后的网关定义。</param>
    /// <returns>角色、分支和目标集合均有效时返回 <see langword="true"/>。</returns>
    public static bool TryRead(JsonElement config, out WorkflowParallelGatewayDefinition? definition)
    {
        definition = null;
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("gatewayRoleKey", out var roleElement) ||
            roleElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return roleElement.GetString() switch
        {
            "fork" => TryReadFork(config, out definition),
            "join" => TryReadJoin(config, out definition),
            _ => false,
        };
    }

    /// <summary>读取并行分叉节点配置。</summary>
    /// <param name="config">分叉节点配置。</param>
    /// <param name="definition">解析后的分叉定义。</param>
    /// <returns>分支数量、键和目标均闭合时返回 <see langword="true"/>。</returns>
    private static bool TryReadFork(JsonElement config, out WorkflowParallelGatewayDefinition? definition)
    {
        definition = null;
        if (!HasOnlyProperties(config, "nodeName", "gatewayRoleKey", "joinNodeKey", "branches") ||
            !TryReadOptionalNodeName(config) ||
            !TryReadStableKey(config, "joinNodeKey", out var joinNodeKey) ||
            !config.TryGetProperty("branches", out var branchesElement) ||
            branchesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var branchElements = branchesElement.EnumerateArray().ToArray();
        if (branchElements.Length is < MinimumBranchCount or > MaximumBranchCount)
        {
            return false;
        }

        var branches = new List<WorkflowParallelBranchDefinition>(branchElements.Length);
        var branchKeys = new HashSet<string>(StringComparer.Ordinal);
        var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branchElement in branchElements)
        {
            if (!TryReadBranch(branchElement, out var branch) ||
                !branchKeys.Add(branch!.BranchKey) ||
                !targetKeys.Add(branch.NextNodeKey))
            {
                return false;
            }

            branches.Add(branch);
        }

        definition = new WorkflowParallelGatewayDefinition(
            WorkflowParallelGatewayRole.Fork,
            joinNodeKey,
            null,
            branches);
        return true;
    }

    /// <summary>读取并行汇合节点配置。</summary>
    /// <param name="config">汇合节点配置。</param>
    /// <param name="definition">解析后的汇合定义。</param>
    /// <returns>分叉引用和唯一后继均有效时返回 <see langword="true"/>。</returns>
    private static bool TryReadJoin(JsonElement config, out WorkflowParallelGatewayDefinition? definition)
    {
        definition = null;
        if (!HasOnlyProperties(config, "nodeName", "gatewayRoleKey", "forkNodeKey", "nextNodeKeys") ||
            !TryReadOptionalNodeName(config) ||
            !TryReadStableKey(config, "forkNodeKey", out var forkNodeKey) ||
            !TryReadSingleNext(config, out var nextNodeKey))
        {
            return false;
        }

        definition = new WorkflowParallelGatewayDefinition(
            WorkflowParallelGatewayRole.Join,
            null,
            forkNodeKey,
            [],
            nextNodeKey);
        return true;
    }

    /// <summary>读取单个并行分支入口。</summary>
    /// <param name="element">分支 JSON。</param>
    /// <param name="branch">解析后的分支。</param>
    /// <returns>分支键和入口节点键有效时返回 <see langword="true"/>。</returns>
    private static bool TryReadBranch(JsonElement element, out WorkflowParallelBranchDefinition? branch)
    {
        branch = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !HasOnlyProperties(element, "branchKey", "nextNodeKey") ||
            !TryReadStableKey(element, "branchKey", out var branchKey) ||
            !TryReadStableKey(element, "nextNodeKey", out var nextNodeKey))
        {
            return false;
        }

        branch = new WorkflowParallelBranchDefinition(branchKey, nextNodeKey);
        return true;
    }

    /// <summary>确认对象只包含允许的属性键。</summary>
    /// <param name="element">待检查对象。</param>
    /// <param name="allowed">允许出现的属性名。</param>
    /// <returns>没有未知属性时返回 <see langword="true"/>。</returns>
    private static bool HasOnlyProperties(JsonElement element, params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        return element.EnumerateObject().All(property => allowedSet.Contains(property.Name));
    }

    /// <summary>读取可选节点展示名称。</summary>
    /// <param name="config">节点配置。</param>
    /// <returns>缺失或合法字符串时返回 <see langword="true"/>。</returns>
    private static bool TryReadOptionalNodeName(JsonElement config)
    {
        if (!config.TryGetProperty("nodeName", out var nodeName))
        {
            return true;
        }

        return nodeName.ValueKind == JsonValueKind.String &&
               nodeName.GetString() is { Length: > 0 and <= MaximumKeyLength };
    }

    /// <summary>读取稳定节点或分支键。</summary>
    /// <param name="element">配置对象。</param>
    /// <param name="propertyName">属性名。</param>
    /// <param name="key">解析后的键。</param>
    /// <returns>键符合稳定标识符规则时返回 <see langword="true"/>。</returns>
    private static bool TryReadStableKey(JsonElement element, string propertyName, out string key)
    {
        key = string.Empty;
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        key = value.GetString() ?? string.Empty;
        return WorkflowNodeKeyValidator.IsValid(key);
    }

    /// <summary>从汇合节点配置读取唯一后继。</summary>
    /// <param name="config">汇合节点配置。</param>
    /// <param name="nextNodeKey">唯一后继节点键。</param>
    /// <returns>恰好包含一个非空后继时返回 <see langword="true"/>。</returns>
    private static bool TryReadSingleNext(JsonElement config, out string nextNodeKey)
    {
        nextNodeKey = string.Empty;
        if (!config.TryGetProperty("nextNodeKeys", out var keys) ||
            keys.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = keys.EnumerateArray().ToArray();
        if (values is not [var value] || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        nextNodeKey = value.GetString() ?? string.Empty;
        return WorkflowNodeKeyValidator.IsValid(nextNodeKey);
    }
}

/// <summary>并行网关角色。</summary>
internal enum WorkflowParallelGatewayRole
{
    /// <summary>分叉：同时激活多个分支。</summary>
    Fork,

    /// <summary>汇合：等待全部分支到达后继续。</summary>
    Join,
}

/// <summary>不可变并行网关定义。</summary>
/// <param name="Role">网关角色。</param>
/// <param name="JoinNodeKey">分叉节点引用的汇合节点键。</param>
/// <param name="ForkNodeKey">汇合节点引用的分叉节点键。</param>
/// <param name="Branches">分叉节点的有序分支集合。</param>
/// <param name="NextNodeKey">汇合节点的唯一后继。</param>
internal sealed record WorkflowParallelGatewayDefinition(
    WorkflowParallelGatewayRole Role,
    string? JoinNodeKey,
    string? ForkNodeKey,
    IReadOnlyList<WorkflowParallelBranchDefinition> Branches,
    string? NextNodeKey = null)
{
    /// <summary>获取分叉节点按稳定键索引的分支集合。</summary>
    public IReadOnlyDictionary<string, WorkflowParallelBranchDefinition> BranchesByKey =>
        Branches.ToDictionary(branch => branch.BranchKey, StringComparer.Ordinal);
}

/// <summary>单个并行分支入口定义。</summary>
/// <param name="BranchKey">稳定分支键。</param>
/// <param name="NextNodeKey">分支入口节点键。</param>
internal sealed record WorkflowParallelBranchDefinition(string BranchKey, string NextNodeKey);

/// <summary>稳定节点键校验器，供网关配置与编译器复用。</summary>
internal static class WorkflowNodeKeyValidator
{
    /// <summary>验证节点键是否符合稳定标识符规则。</summary>
    /// <param name="key">待验证键。</param>
    /// <returns>键非空且符合规则时返回 <see langword="true"/>。</returns>
    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        key.Length <= 128 &&
        System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9_.-]*$");
}
