using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>解析包容网关分叉与汇合节点的闭合配置。</summary>
internal static class WorkflowInclusiveGatewayConfiguration
{
    private const int MinimumBranchCount = 1;
    private const int MaximumBranchCount = 8;
    private const int MaximumKeyLength = 128;

    /// <summary>读取包容网关节点配置。</summary>
    /// <param name="config">包容网关节点配置。</param>
    /// <param name="formSchema">可选的已发布表单架构。</param>
    /// <param name="definition">成功解析后的网关定义。</param>
    /// <returns>角色、条件分支和目标集合均有效时返回 <see langword="true"/>。</returns>
    public static bool TryRead(
        JsonElement config,
        WorkflowFormSchema? formSchema,
        out WorkflowInclusiveGatewayDefinition? definition)
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
            "fork" => TryReadFork(config, formSchema, out definition),
            "join" => TryReadJoin(config, out definition),
            _ => false,
        };
    }

    /// <summary>读取包容分叉节点配置。</summary>
    /// <param name="config">分叉节点配置。</param>
    /// <param name="formSchema">可选的已发布表单架构。</param>
    /// <param name="definition">解析后的分叉定义。</param>
    /// <returns>条件分支、默认出口与汇合引用均闭合时返回 <see langword="true"/>。</returns>
    private static bool TryReadFork(
        JsonElement config,
        WorkflowFormSchema? formSchema,
        out WorkflowInclusiveGatewayDefinition? definition)
    {
        definition = null;
        if (!HasOnlyProperties(config, "nodeName", "gatewayRoleKey", "joinNodeKey", "branches", "defaultNextNodeKey") ||
            !TryReadOptionalNodeName(config) ||
            !TryReadStableKey(config, "joinNodeKey", out var joinNodeKey) ||
            !TryReadStableKey(config, "defaultNextNodeKey", out var defaultNextNodeKey) ||
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

        var fields = formSchema?.Sections
            .SelectMany(section => section.Fields)
            .ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var branches = new List<WorkflowExclusiveGatewayBranch>(branchElements.Length);
        var branchKeys = new HashSet<string>(StringComparer.Ordinal);
        var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branchElement in branchElements)
        {
            if (!WorkflowExclusiveGatewayConfiguration.TryReadConditionalBranch(branchElement, fields, out var branch) ||
                !branchKeys.Add(branch!.BranchKey) ||
                !targetKeys.Add(branch.NextNodeKey))
            {
                return false;
            }

            branches.Add(branch);
        }

        if (!targetKeys.Add(defaultNextNodeKey))
        {
            return false;
        }

        definition = new WorkflowInclusiveGatewayDefinition(
            WorkflowInclusiveGatewayRole.Fork,
            joinNodeKey,
            null,
            branches,
            defaultNextNodeKey);
        return true;
    }

    /// <summary>读取包容汇合节点配置。</summary>
    /// <param name="config">汇合节点配置。</param>
    /// <param name="definition">解析后的汇合定义。</param>
    /// <returns>分叉引用和唯一后继均有效时返回 <see langword="true"/>。</returns>
    private static bool TryReadJoin(JsonElement config, out WorkflowInclusiveGatewayDefinition? definition)
    {
        definition = null;
        if (!HasOnlyProperties(config, "nodeName", "gatewayRoleKey", "forkNodeKey", "nextNodeKeys") ||
            !TryReadOptionalNodeName(config) ||
            !TryReadStableKey(config, "forkNodeKey", out var forkNodeKey) ||
            !TryReadSingleNext(config, out var nextNodeKey))
        {
            return false;
        }

        definition = new WorkflowInclusiveGatewayDefinition(
            WorkflowInclusiveGatewayRole.Join,
            null,
            forkNodeKey,
            [],
            nextNodeKey);
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

/// <summary>包容网关角色。</summary>
internal enum WorkflowInclusiveGatewayRole
{
    /// <summary>分叉：按条件激活一个或多个分支。</summary>
    Fork,

    /// <summary>汇合：等待全部已激活分支到达后继续。</summary>
    Join,
}

/// <summary>不可变包容网关定义。</summary>
/// <param name="Role">网关角色。</param>
/// <param name="JoinNodeKey">分叉节点引用的汇合节点键。</param>
/// <param name="ForkNodeKey">汇合节点引用的分叉节点键。</param>
/// <param name="Branches">分叉节点的有序条件分支集合。</param>
/// <param name="DefaultNextNodeKey">无条件命中时使用的默认分支目标；汇合节点复用为唯一后继。</param>
internal sealed record WorkflowInclusiveGatewayDefinition(
    WorkflowInclusiveGatewayRole Role,
    string? JoinNodeKey,
    string? ForkNodeKey,
    IReadOnlyList<WorkflowExclusiveGatewayBranch> Branches,
    string DefaultNextNodeKey)
{
    /// <summary>按实例表单值选择全部成立分支；无命中时回落到默认分支。</summary>
    /// <param name="values">实例绑定且已通过表单协议校验的字段值。</param>
    /// <param name="selections">需要激活的分支集合，至少包含一个出口。</param>
    /// <returns>全部条件都可安全求值时返回 <see langword="true"/>。</returns>
    public bool TrySelectBranches(
        IReadOnlyDictionary<string, JsonElement> values,
        out IReadOnlyList<WorkflowExclusiveGatewaySelection> selections)
    {
        var selected = new List<WorkflowExclusiveGatewaySelection>();
        foreach (var branch in Branches)
        {
            if (!branch.Condition.TryEvaluate(values, out var matched))
            {
                selections = [];
                return false;
            }

            if (matched)
            {
                selected.Add(new WorkflowExclusiveGatewaySelection(branch.BranchKey, branch.NextNodeKey));
            }
        }

        if (selected.Count == 0)
        {
            selected.Add(new WorkflowExclusiveGatewaySelection("default", DefaultNextNodeKey));
        }

        selections = selected;
        return true;
    }
}
