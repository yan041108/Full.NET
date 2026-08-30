using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

internal sealed record WorkflowNodeFormView(
    WorkflowFormSchema Schema,
    Dictionary<string, JsonElement> Values,
    Dictionary<string, string> FieldPolicies);

internal sealed class WorkflowNodeFieldPolicy
{
    private readonly IReadOnlyDictionary<string, string> _fields;

    private WorkflowNodeFieldPolicy(IReadOnlyDictionary<string, string> fields)
    {
        _fields = fields;
    }

    public static bool TryResolve(
        string canonicalJson,
        string nodeKey,
        WorkflowFormSchema schema,
        out WorkflowNodeFieldPolicy? policy)
    {
        policy = null;
        using var document = JsonDocument.Parse(canonicalJson);
        if (!document.RootElement.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        JsonElement? matchedNode = null;
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind == JsonValueKind.Object &&
                node.TryGetProperty("nodeKey", out var candidateKey) &&
                candidateKey.ValueKind == JsonValueKind.String &&
                candidateKey.GetString() == nodeKey)
            {
                matchedNode = node;
                break;
            }
        }

        if (matchedNode is not { } matched ||
            !matched.TryGetProperty("nodeTypeKey", out var nodeType) ||
            nodeType.GetString() != "human.approval" ||
            !matched.TryGetProperty("config", out var config) ||
            config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var formFields = schema.Sections.SelectMany(section => section.Fields)
            .ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var resolved = formFields.Values.ToDictionary(
            field => field.FieldKey,
            field => field.Required ? "required" : "editable",
            StringComparer.Ordinal);
        if (config.TryGetProperty("fieldPolicies", out var configured))
        {
            if (configured.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var item in configured.EnumerateObject())
            {
                if (!formFields.ContainsKey(item.Name) ||
                    item.Value.ValueKind != JsonValueKind.String ||
                    item.Value.GetString() is not ("hidden" or "readOnly" or "editable" or "required"))
                {
                    return false;
                }

                resolved[item.Name] = item.Value.GetString()!;
            }
        }

        policy = new WorkflowNodeFieldPolicy(resolved);
        return true;
    }

    public WorkflowNodeFormView CreateView(
        WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var sections = schema.Sections.Select(section => new WorkflowFormSection(
            section.SectionKey,
            section.Fields.Where(field => IsVisible(field.FieldKey)).ToArray())).ToArray();
        var visibleValues = values
            .Where(item => IsVisible(item.Key) && _fields.ContainsKey(item.Key))
            .ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);
        var visiblePolicies = _fields
            .Where(item => item.Value != "hidden")
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return new WorkflowNodeFormView(
            new WorkflowFormSchema(schema.SchemaVersion, schema.AdapterVersion, sections),
            visibleValues,
            visiblePolicies);
    }

    public bool TryApplyPatch(
        WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> values,
        JsonElement patch,
        out Dictionary<string, JsonElement>? patched)
    {
        var merged = values.ToDictionary(
            item => item.Key,
            item => item.Value.Clone(),
            StringComparer.Ordinal);
        if (patch.ValueKind != JsonValueKind.Object)
        {
            patched = null;
            return false;
        }

        foreach (var property in patch.EnumerateObject())
        {
            if (!_fields.TryGetValue(property.Name, out var access) ||
                access is not ("editable" or "required"))
            {
                patched = null;
                return false;
            }

            merged[property.Name] = property.Value.Clone();
        }

        if (!WorkflowFormValueValidator.Validate(schema, merged) ||
            _fields.Where(item => item.Value == "required")
                .Any(item => !merged.TryGetValue(item.Key, out var value) ||
                             value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
        {
            patched = null;
            return false;
        }

        patched = merged;
        return true;
    }

    private bool IsVisible(string fieldKey) =>
        _fields.TryGetValue(fieldKey, out var access) && access != "hidden";
}
