using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>子流程只引用固定版本和显式字段映射；文件仍由真实父子投影及Files授权接纳。</summary>
internal sealed record WorkflowSubprocessPolicy(Guid DefinitionVersionId, IReadOnlyDictionary<string, string> InputFields,
    IReadOnlyDictionary<string, string> OutputFields, IReadOnlyDictionary<string, string> FieldPolicies, WorkflowMultiInstancePolicy? Multi = null)
{
    internal const string NodeType = "process.subprocess";
    internal const int MaximumMappings = 32;
    internal const int MaximumInputBytes = 131072;
    internal const int MaximumDepth = 8;

    public static bool TryRead(JsonElement config, WorkflowFormSchema? parentSchema, [NotNullWhen(true)] out WorkflowSubprocessPolicy? policy)
    {
        policy = null;
        if (!Unique(config) || !config.TryGetProperty("subprocessPolicy", out var value) || !Unique(value) ||
            value.EnumerateObject().Count() != 3 || value.EnumerateObject().Any(item => item.Name is not ("definitionVersionId" or "inputFields" or "outputFields")) ||
            !value.TryGetProperty("definitionVersionId", out var version) || !ReadId(version, out var id) ||
            !Map(value, "inputFields", out var inputs) || !Map(value, "outputFields", out var outputs) ||
            !config.TryGetProperty("fieldPolicies", out var access) || !Unique(access)) return false;
        var fields = parentSchema?.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var grants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in access.EnumerateObject())
        {
            if (!Key(item.Name) || fields is not null && !fields.ContainsKey(item.Name) || item.Value.ValueKind != JsonValueKind.String ||
                item.Value.GetString() is not ("hidden" or "readOnly" or "editable" or "required")) return false;
            grants.Add(item.Name, item.Value.GetString()!);
        }
        if (inputs!.Values.Any(key => !grants.TryGetValue(key, out var grant) || grant == "hidden") ||
            outputs!.Keys.Any(key => !grants.TryGetValue(key, out var grant) || grant is not ("editable" or "required"))) return false;
        if (config.TryGetProperty("boundSubprocess", out var bound) && !WorkflowBoundSubprocess.TryRead(bound, id, out _)) return false;
        WorkflowMultiInstancePolicy? multi = null;
        if (config.TryGetProperty("multiInstancePolicy", out var multiJson) &&
            (!WorkflowMultiInstancePolicy.TryRead(multiJson, parentSchema, out multi) ||
             multi.FieldKey is { } source && (!grants.TryGetValue(source, out var sourceGrant) || sourceGrant == "hidden") ||
             multi.ItemFieldKey is { } itemField && inputs.ContainsKey(itemField) ||
             !multi.OutputReducers.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(outputs.Keys))) return false;
        policy = new(id, inputs, outputs, grants, multi); return true;
    }

    /// <summary>发布验证双方字段和必要输入，实际值仍在激活前按固定子Schema完整复核。</summary>
    public bool MatchesSchemas(WorkflowFormSchema parent, WorkflowFormSchema child)
    {
        var source = parent.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var target = child.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        foreach (var pair in InputFields)
            if (!target.TryGetValue(pair.Key, out var childField) || !source.TryGetValue(pair.Value, out var parentField) || !SameSupportedType(parentField, childField)) return false;
        foreach (var pair in OutputFields)
            if (!source.TryGetValue(pair.Key, out var parentField) || !target.TryGetValue(pair.Value, out var childField) || !SameSupportedType(parentField, childField) ||
                parentField.Required && !childField.Required) return false;
        if (Multi?.ItemFieldKey is { } itemKey && (!target.TryGetValue(itemKey, out var itemField) ||
            itemField.FieldTypeKey is not ("text" or "textarea" or "select" or "radio"))) return false;
        if (Multi is { } multi && OutputFields.Any(pair => !WorkflowMultiInstanceOutputReducer.Supports(source[pair.Key].FieldTypeKey, multi.OutputReducers[pair.Key]))) return false;
        return target.Values.Where(field => field.Required).All(field => InputFields.ContainsKey(field.FieldKey) || field.FieldKey == Multi?.ItemFieldKey);
    }

    public bool TryCreateInput(IReadOnlyDictionary<string, JsonElement> values, WorkflowFormSchema childSchema, out string? json,
        WorkflowMultiInstanceSeed? seed = null)
    {
        json = null; var input = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in InputFields.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!values.TryGetValue(pair.Value, out var value) || value.ValueKind == JsonValueKind.Undefined) return false;
            input.Add(pair.Key, value.Clone());
        }
        if (Multi?.ItemFieldKey is { } itemKey)
        {
            if (seed is null) return false;
            try { using var item = JsonDocument.Parse(seed.ValueJson); input.Add(itemKey, item.RootElement.Clone()); }
            catch (JsonException) { return false; }
        }
        if (!WorkflowFormValueValidator.Validate(childSchema, input)) return false;
        var serialized = JsonSerializer.Serialize(input, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if (Encoding.UTF8.GetByteCount(serialized) > MaximumInputBytes) return false;
        json = serialized; return true;
    }

    public bool TryCreateOutput(IReadOnlyDictionary<string, JsonElement> childValues, WorkflowFormSchema parentSchema,
        out IReadOnlyDictionary<string, JsonElement> patch)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal); patch = result;
        var fields = parentSchema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        foreach (var pair in OutputFields.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!fields.TryGetValue(pair.Key, out var field) || !childValues.TryGetValue(pair.Value, out var value) ||
                value.ValueKind == JsonValueKind.Undefined || value.ValueKind == JsonValueKind.Null && field.Required ||
                value.ValueKind != JsonValueKind.Null && !WorkflowFormValueValidator.IsFieldValueValid(field, value)) return false;
            result.Add(pair.Key, value.Clone());
        }
        return true;
    }

    private static bool SameSupportedType(WorkflowFormField left, WorkflowFormField right)
    {
        if (left.FieldTypeKey != right.FieldTypeKey) return false;
        if (left.FieldTypeKey == "attachment") return WorkflowFormAttachmentConstraints.TryRead(left, out _, out _, out _) && WorkflowFormAttachmentConstraints.TryRead(right, out _, out _, out _);
        if (left.FieldTypeKey == "subtable")
        {
            if (!WorkflowFormSubtableConstraints.TryRead(left, out _, out var source) || !WorkflowFormSubtableConstraints.TryRead(right, out _, out var target)) return false;
            return source.Count == target.Count && source.All(column => target.Any(other => other.ColumnKey == column.ColumnKey && other.FieldTypeKey == column.FieldTypeKey));
        }
        return left.FieldTypeKey is "text" or "textarea" or "integer" or "decimal" or "money" or "date" or "time" or "datetime" or "radio" or "select" or "checkbox" or "switch";
    }
    private static bool Map(JsonElement value, string key, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? map)
    {
        map = null;
        if (!value.TryGetProperty(key, out var data) || !Unique(data) || data.EnumerateObject().Count() > MaximumMappings) return false;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateObject())
        {
            if (!Key(item.Name) || item.Value.ValueKind != JsonValueKind.String || !Key(item.Value.GetString())) return false;
            result.Add(item.Name, item.Value.GetString()!);
        }
        map = result; return true;
    }
    internal static bool Unique(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Count() == value.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count();
    internal static bool Key(string? value) => value?.Length <= 64 && WorkflowNodeKeyValidator.IsValid(value) && value is not ("__proto__" or "prototype" or "constructor");
    internal static bool ReadId(JsonElement value, out Guid id)
    { id = default; return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out id) && id != Guid.Empty; }
}

/// <summary>发布时从本作用域真实资产派生的不可变绑定，普通草稿中的声明会被权威资产覆盖。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WorkflowBoundSubprocess(Guid DefinitionVersionId, Guid FormVersionId, string DefinitionHash, string FormHash,
    int ActivityBudget, int MaximumDepth, bool HasHumanApproval)
{
    public static bool TryRead(JsonElement value, Guid expectedVersion, [NotNullWhen(true)] out WorkflowBoundSubprocess? bound)
    {
        bound = null;
        if (!WorkflowSubprocessPolicy.Unique(value) || value.EnumerateObject().Count() != 7 ||
            value.EnumerateObject().Any(item => item.Name is not ("definitionVersionId" or "formVersionId" or "definitionHash" or "formHash" or "activityBudget" or "maximumDepth" or "hasHumanApproval")) ||
            !value.TryGetProperty("definitionVersionId", out var definition) || !WorkflowSubprocessPolicy.ReadId(definition, out var definitionId) || definitionId != expectedVersion ||
            !value.TryGetProperty("formVersionId", out var form) || !WorkflowSubprocessPolicy.ReadId(form, out var formId) ||
            !value.TryGetProperty("definitionHash", out var definitionHash) || !WorkflowBusinessCommandPolicy.ValidHash(definitionHash) ||
            !value.TryGetProperty("formHash", out var formHash) || !WorkflowBusinessCommandPolicy.ValidHash(formHash) ||
            !value.TryGetProperty("activityBudget", out var budget) || budget.ValueKind != JsonValueKind.Number || !budget.TryGetInt32(out var activityBudget) ||
            activityBudget is < 2 or > WorkflowActivityScheduler.InstanceActivityBudget ||
            !value.TryGetProperty("maximumDepth", out var depth) || depth.ValueKind != JsonValueKind.Number || !depth.TryGetInt32(out var maximumDepth) ||
            maximumDepth is < 1 or > WorkflowSubprocessPolicy.MaximumDepth ||
            !value.TryGetProperty("hasHumanApproval", out var approval) || approval.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        bound = new(definitionId, formId, definitionHash.GetString()!, formHash.GetString()!, activityBudget, maximumDepth, approval.GetBoolean()); return true;
    }
}
