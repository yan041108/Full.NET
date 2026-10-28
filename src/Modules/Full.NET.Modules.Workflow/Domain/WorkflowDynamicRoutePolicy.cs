using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>路由只返回固定目标引用；规则有序，决策字段必须精确命中允许目标，不解释任意URL或脚本。</summary>
internal sealed record WorkflowDynamicRoutePolicy(string ModeKey, string? DecisionFieldKey, IReadOnlyList<WorkflowRouteTarget> Targets)
{
    internal const string NodeType = "control.route";
    internal const int MaximumTargets = 8;

    public bool TrySelect(IReadOnlyDictionary<string, JsonElement> values, out WorkflowRouteTarget? target)
    {
        target = null;
        if (ModeKey == "rules")
        {
            foreach (var candidate in Targets)
            {
                if (!candidate.Condition!.TryEvaluate(values, out var matched)) return false;
                if (matched) { target = candidate; break; }
            }
            return true;
        }
        if (DecisionFieldKey is null || !values.TryGetValue(DecisionFieldKey, out var decision) || decision.ValueKind != JsonValueKind.String) return false;
        var selected = decision.GetString(); target = Targets.FirstOrDefault(candidate => candidate.TargetKey == selected);
        return target is not null;
    }

    public static bool TryRead(JsonElement config, WorkflowFormSchema? schema, out WorkflowDynamicRoutePolicy? policy)
    {
        policy = null;
        if (!Only(config, "nodeName", "nextNodeKeys", "routePolicy") ||
            !config.TryGetProperty("nextNodeKeys", out var normal) || normal.ValueKind != JsonValueKind.Array || normal.GetArrayLength() != 1 ||
            normal[0].ValueKind != JsonValueKind.String || !WorkflowNodeKeyValidator.IsValid(normal[0].GetString()) ||
            config.TryGetProperty("nodeName", out var name) && (name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 128) ||
            !config.TryGetProperty("routePolicy", out var route) || !Only(route, "modeKey", "decisionFieldKey", "targets") ||
            !route.TryGetProperty("modeKey", out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("rules" or "decision_field") ||
            !route.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() is < 1 or > MaximumTargets) return false;
        var modeKey = mode.GetString()!;
        var fields = schema?.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var hasField = route.TryGetProperty("decisionFieldKey", out var decisionField); string? fieldKey = null;
        if (modeKey == "rules" && hasField || modeKey == "decision_field" && (!hasField || decisionField.ValueKind != JsonValueKind.String || !WorkflowNodeKeyValidator.IsValid(fieldKey = decisionField.GetString()))) return false;
        if (fieldKey is not null && fields is not null && (!fields.TryGetValue(fieldKey, out var field) || field.FieldTypeKey != "select")) return false;
        var result = new List<WorkflowRouteTarget>(); var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        var nodeKeys = new HashSet<string>(StringComparer.Ordinal); var regionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in targets.EnumerateArray())
        {
            if (!Only(item, "targetKey", "nodeKey", "regionKey", "maximumIterations", "condition") ||
                !Key(item, "targetKey", 64, out var targetKey) || !Key(item, "nodeKey", 128, out var nodeKey) || !Key(item, "regionKey", 64, out var regionKey) ||
                !item.TryGetProperty("maximumIterations", out var iterations) || iterations.ValueKind != JsonValueKind.Number || !iterations.TryGetInt32(out var maximum) || maximum is < 1 or > 32 ||
                !targetKeys.Add(targetKey) || !nodeKeys.Add(nodeKey) || !regionKeys.Add(regionKey)) return false;
            var hasCondition = item.TryGetProperty("condition", out var conditionJson); WorkflowExclusiveGatewayCondition? condition = null;
            if (modeKey == "rules" ? !hasCondition || !WorkflowExclusiveGatewayConfiguration.TryReadCondition(conditionJson, fields, out condition) : hasCondition) return false;
            result.Add(new(targetKey, nodeKey, regionKey, maximum, condition));
        }
        // 决策值采用闭合机器键；展示标签与任意文本不能成为运行目标。
        if (fieldKey is not null && fields is not null && result.Any(target => !WorkflowFormValueValidator.IsFieldValueValid(fields[fieldKey], JsonSerializer.SerializeToElement(target.TargetKey, WorkflowJsonSerializerContext.Default.String)))) return false;
        policy = new(modeKey, fieldKey, result.AsReadOnly()); return true;
    }

    private static bool Only(JsonElement value, params string[] allowed) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal)) &&
        value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();
    private static bool Key(JsonElement value, string name, int maximum, out string key)
    {
        key = string.Empty;
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String || property.GetString() is not { } text || text.Length > maximum || !WorkflowNodeKeyValidator.IsValid(text)) return false;
        key = text; return true;
    }
}

/// <summary>区域键独立于128字符节点键；每次运行创建全新的代次及激活身份。</summary>
internal sealed record WorkflowRouteTarget(string TargetKey, string NodeKey, string RegionKey, int MaximumIterations, WorkflowExclusiveGatewayCondition? Condition);
