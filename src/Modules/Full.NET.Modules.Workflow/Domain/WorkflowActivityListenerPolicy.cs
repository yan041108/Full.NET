using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>监听器只绑定已实现的静态动作；完成表示整个活动收敛，个人提交不触发监听器。</summary>
internal sealed record WorkflowActivityListenerPolicy(string ListenerKey, string EventKey)
{
    public const string ActionKey = "notify.initiator";
    public static bool TryRead(string nodeType, JsonElement config, out IReadOnlyList<WorkflowActivityListenerPolicy> listeners)
    {
        listeners = [];
        if (config.ValueKind != JsonValueKind.Object) return false;
        var properties = config.EnumerateObject().Where(item => item.NameEquals("listeners")).ToArray();
        if (properties.Length == 0) return true;
        if (properties.Length != 1 || !WorkflowHumanActivityKinds.IsFormActivity(nodeType)) return false;
        var array = properties[0].Value;
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 8) return false;
        var result = new List<WorkflowActivityListenerPolicy>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return false;
            var fields = item.EnumerateObject().ToArray();
            if (fields.Length != 4 || fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != 4 ||
                fields.Any(field => field.Name is not ("listenerKey" or "eventKey" or "actionKey" or "actionVersion"))) return false;
            if (!item.TryGetProperty("listenerKey", out var key) || key.ValueKind != JsonValueKind.String ||
                key.GetString()!.Length > 64 || !WorkflowNodeKeyValidator.IsValid(key.GetString()) || !keys.Add(key.GetString()!)) return false;
            if (!item.TryGetProperty("eventKey", out var phase) || phase.ValueKind != JsonValueKind.String || phase.GetString() is not ("completed" or "rejected") ||
                !item.TryGetProperty("actionKey", out var action) || action.ValueKind != JsonValueKind.String || action.GetString() != ActionKey ||
                !item.TryGetProperty("actionVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1) return false;
            result.Add(new(key.GetString()!, phase.GetString()!));
        }
        listeners = result.AsReadOnly();
        return true;
    }
}
