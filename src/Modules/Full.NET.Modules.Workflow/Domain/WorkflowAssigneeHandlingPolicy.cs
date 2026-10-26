using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>无人和本人处置只来自固定发布配置；替补链最多三次，禁止通过嵌套策略形成递归。</summary>
internal sealed record WorkflowAssigneeHandlingPolicy(WorkflowAssigneeHandlingAction WhenEmpty, WorkflowAssigneeHandlingAction WhenInitiator)
{
    public const int MaximumFallbackAttempts = 3;
    public static WorkflowAssigneeHandlingPolicy Default { get; } = new(new("error", []), new("keep", []));

    public static bool TryRead(JsonElement value, out WorkflowAssigneeHandlingPolicy? policy)
    {
        policy = null;
        if (value.ValueKind != JsonValueKind.Object) return false;
        var keys = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (keys.Length is < 1 or > 2 || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            keys.Any(key => key is not ("whenEmpty" or "whenInitiator"))) return false;
        var empty = Default.WhenEmpty; var initiator = Default.WhenInitiator;
        if (value.TryGetProperty("whenEmpty", out var emptyJson) && !TryReadAction(emptyJson, false, out empty)) return false;
        if (value.TryGetProperty("whenInitiator", out var initiatorJson) && !TryReadAction(initiatorJson, true, out initiator)) return false;
        policy = new(empty, initiator); return true;
    }

    private static bool TryReadAction(JsonElement value, bool forInitiator, out WorkflowAssigneeHandlingAction action)
    {
        action = forInitiator ? Default.WhenInitiator : Default.WhenEmpty;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("actionKey", out var keyJson) ||
            keyJson.ValueKind != JsonValueKind.String || keyJson.GetString() is not { } key) return false;
        if (forInitiator ? key is not ("keep" or "skip" or "assign") : key is not ("error" or "suspend" or "auto_pass" or "auto_reject" or "assign")) return false;
        var expected = key == "assign" ? new[] { "actionKey", "fallbackPolicies" } : new[] { "actionKey" };
        if (!value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal))) return false;
        var fallbacks = new List<WorkflowAssigneePolicy>();
        if (key == "assign")
        {
            var list = value.GetProperty("fallbackPolicies");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > MaximumFallbackAttempts) return false;
            foreach (var fallback in list.EnumerateArray())
            {
                // 替补只引用当前权威目录；不引入另一份启动选择、可变表单或尚未完成的前序依赖。
                if (fallback.ValueKind != JsonValueKind.Object || fallback.EnumerateObject().Count() != 1 ||
                    !fallback.TryGetProperty("sources", out _) || !WorkflowAssigneePolicy.TryReadPolicy(fallback, out var parsed) ||
                    parsed.Sources.Any(source => source.ResolverKindKey is WorkflowAssigneePolicy.FormUsers or WorkflowAssigneePolicy.FormUnits
                        or WorkflowAssigneePolicy.PreviousActors or WorkflowAssigneePolicy.StarterSelectedUsers)) return false;
                fallbacks.Add(parsed);
            }
        }
        action = new(key, fallbacks); return true;
    }
}

/// <summary>静态替补按发布顺序尝试；管理员也以明确配置的受信成员来源表达，不查询隐含超级管理员集合。</summary>
internal sealed record WorkflowAssigneeHandlingAction(string ActionKey, IReadOnlyList<WorkflowAssigneePolicy> FallbackPolicies);
