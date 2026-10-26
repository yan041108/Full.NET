using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>活动最终结果只回填显式授权的布尔字段；个人中间票和未收敛动作不产生结果。</summary>
internal sealed record WorkflowResultCallbackPolicy(string FieldKey)
{
    public static bool TryRead(string nodeTypeKey, JsonElement config, WorkflowFormSchema? schema, out WorkflowResultCallbackPolicy? policy)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object) return false;
        if (!config.TryGetProperty("resultCallbackFieldKey", out var target)) return true;
        if (!WorkflowHumanActivityKinds.IsFormActivity(nodeTypeKey) || target.ValueKind != JsonValueKind.String ||
            target.GetString() is not { Length: > 0 and <= 64 } key || !WorkflowNodeKeyValidator.IsValid(key) || key is "__proto__" or "prototype" or "constructor" ||
            config.EnumerateObject().Count(item => item.Name == "resultCallbackFieldKey") != 1 ||
            !config.TryGetProperty("fieldPolicies", out var grants) || grants.ValueKind != JsonValueKind.Object ||
            grants.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != grants.EnumerateObject().Count() ||
            !grants.TryGetProperty(key, out var access) || access.ValueKind != JsonValueKind.String || access.GetString() is not ("editable" or "required")) return false;
        if (schema is not null)
        {
            if (!WorkflowFormCompiler.Compile(schema).IsSuccess) return false;
            var fields = schema.Sections.SelectMany(section => section.Fields).Where(field => field.FieldKey == key).ToArray();
            if (fields is not [var field] || field.FieldTypeKey != "switch") return false;
        }
        policy = new(key); return true;
    }

    /// <summary>派生结果保存为独立副本，不能改写调用方个人提交或其他分支字段。</summary>
    public Dictionary<string, JsonElement> Apply(IReadOnlyDictionary<string, JsonElement> values, bool accepted)
    {
        var result = values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        using var document = JsonDocument.Parse(accepted ? "true" : "false");
        result[FieldKey] = document.RootElement.Clone(); return result;
    }
}
