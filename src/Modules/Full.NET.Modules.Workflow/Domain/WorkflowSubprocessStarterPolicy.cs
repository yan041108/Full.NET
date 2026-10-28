using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发布的发起人来源是受控委托；只解析稳定身份，活动状态由事务外可信目录再次确认。</summary>
internal sealed record WorkflowSubprocessStarterPolicy(string SourceKey, Guid? UserId, string? FieldKey, string? ResultFieldKey)
{
    public static bool TryRead(JsonElement value, WorkflowFormSchema? parent, WorkflowMultiInstancePolicy? multi,
        [NotNullWhen(true)] out WorkflowSubprocessStarterPolicy? policy)
    {
        policy = null;
        if (!WorkflowSubprocessPolicy.Unique(value) || value.EnumerateObject().Count() is < 1 or > 2 ||
            value.EnumerateObject().Any(item => item.Name is not ("source" or "resultFieldKey")) ||
            !value.TryGetProperty("source", out var source) || !WorkflowSubprocessPolicy.Unique(source) ||
            !source.TryGetProperty("kindKey", out var kind) || kind.ValueKind != JsonValueKind.String) return false;
        var sourceKey = kind.GetString(); Guid? user = null; string? field = null; string? result = null;
        switch (sourceKey)
        {
            case "parent":
                if (source.EnumerateObject().Count() != 1) return false;
                break;
            case "specified_user":
                if (source.EnumerateObject().Count() != 2 || !source.TryGetProperty("userId", out var id) || !WorkflowSubprocessPolicy.ReadId(id, out var parsed)) return false;
                user = parsed; break;
            case "item_user":
                if (source.EnumerateObject().Count() != 1 || multi?.SourceKey != "role_members") return false;
                break;
            case "field_user":
                if (source.EnumerateObject().Count() != 2 || !source.TryGetProperty("fieldKey", out var fieldJson) || fieldJson.ValueKind != JsonValueKind.String ||
                    !WorkflowSubprocessPolicy.Key(fieldJson.GetString())) return false;
                field = fieldJson.GetString();
                if (parent is not null)
                {
                    var selectedField = parent.Sections.SelectMany(section => section.Fields).SingleOrDefault(item => item.FieldKey == field);
                    if (selectedField?.FieldTypeKey is not ("select" or "radio") || !WorkflowFormChoiceOptions.TryRead(selectedField, out var options) ||
                        options.Any(option => !Guid.TryParseExact(option, "D", out var candidate) || candidate == Guid.Empty)) return false;
                }
                break;
            default: return false;
        }
        if (value.TryGetProperty("resultFieldKey", out var resultJson))
        {
            if (resultJson.ValueKind != JsonValueKind.String || !WorkflowSubprocessPolicy.Key(resultJson.GetString())) return false;
            result = resultJson.GetString();
            if (parent is not null)
            {
                var target = parent.Sections.SelectMany(section => section.Fields).SingleOrDefault(item => item.FieldKey == result);
                if (target?.FieldTypeKey != (multi is null ? "text" : "checkbox")) return false;
                if (multi is not null && (!WorkflowFormChoiceOptions.TryRead(target, out var options) || options.Any(option =>
                        !Guid.TryParseExact(option, "D", out var candidate) || candidate == Guid.Empty || candidate.ToString("D") != option))) return false;
            }
        }
        policy = new(sourceKey!, user, field, result); return true;
    }

    public bool TryResolve(Guid parentStarter, IReadOnlyDictionary<string, JsonElement> input, WorkflowMultiInstanceSeed seed, out Guid starter)
    {
        starter = Guid.Empty;
        if (SourceKey == "parent") starter = parentStarter;
        else if (SourceKey == "specified_user") starter = UserId ?? Guid.Empty;
        else if (SourceKey == "field_user")
        {
            if (FieldKey is null || !input.TryGetValue(FieldKey, out var value) || value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "D", out starter)) return false;
        }
        else if (SourceKey == "item_user")
        {
            try { using var item = JsonDocument.Parse(seed.ValueJson);
                if (item.RootElement.ValueKind != JsonValueKind.String || !Guid.TryParseExact(item.RootElement.GetString(), "D", out starter)) return false; }
            catch (JsonException) { return false; }
        }
        return starter != Guid.Empty;
    }
}
