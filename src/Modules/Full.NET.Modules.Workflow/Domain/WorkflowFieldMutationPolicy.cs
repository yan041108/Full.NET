using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>本地表单字段动作的闭合规则；不执行脚本、算式、动态属性路径或业务表命令。</summary>
internal sealed class WorkflowFieldMutationPolicy
{
    internal const int MaximumAssignments = 16;
    private readonly IReadOnlyList<WorkflowFieldAssignment> assignments;
    private readonly IReadOnlyDictionary<string, string> permissions;
    private readonly WorkflowFormSchema? schema;
    private readonly WorkflowExclusiveGatewayCondition? condition;

    private WorkflowFieldMutationPolicy(IReadOnlyList<WorkflowFieldAssignment> assignments,
        IReadOnlyDictionary<string, string> permissions, WorkflowFormSchema? schema, WorkflowExclusiveGatewayCondition? condition)
    { this.assignments = assignments; this.permissions = permissions; this.schema = schema; this.condition = condition; }

    public IReadOnlyList<string> WrittenFields => assignments.Select(item => item.FieldKey).ToArray();
    public bool CanRead(string fieldKey) => permissions.TryGetValue(fieldKey, out var access) && access != "hidden";

    /// <summary>写入字段必须逐项显式授权；复制绑定固定Schema，常量不得借字符串隐式转换类型。</summary>
    public static bool TryRead(string nodeTypeKey, JsonElement config, WorkflowFormSchema? schema,
        out WorkflowFieldMutationPolicy? policy)
    {
        policy = null;
        if (nodeTypeKey is not ("data.modify" or "data.clear") || config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("fieldPolicies", out var access) || !UniqueObject(access) ||
            !config.TryGetProperty("mutationPolicy", out var value) || !UniqueObject(value) ||
            value.EnumerateObject().Any(item => item.Name is not ("assignments" or "fieldKeys" or "condition"))) return false;
        var fields = schema?.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var granted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in access.EnumerateObject())
        {
            if (!ValidFieldKey(item.Name) || fields is not null && !fields.ContainsKey(item.Name) ||
                item.Value.ValueKind != JsonValueKind.String || item.Value.GetString() is not ("hidden" or "readOnly" or "editable" or "required")) return false;
            granted.Add(item.Name, item.Value.GetString()!);
        }
        var result = new List<WorkflowFieldAssignment>();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        if (nodeTypeKey == "data.clear")
        {
            if (value.TryGetProperty("assignments", out _) || !value.TryGetProperty("fieldKeys", out var keys) ||
                keys.ValueKind != JsonValueKind.Array || keys.GetArrayLength() is < 1 or > MaximumAssignments) return false;
            foreach (var key in keys.EnumerateArray())
            {
                if (key.ValueKind != JsonValueKind.String || !ValidFieldKey(key.GetString()) || !targets.Add(key.GetString()!)) return false;
                result.Add(new(key.GetString()!, null, NullValue()));
            }
        }
        else
        {
            if (value.TryGetProperty("fieldKeys", out _) || !value.TryGetProperty("assignments", out var rows) ||
                rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() is < 1 or > MaximumAssignments) return false;
            foreach (var row in rows.EnumerateArray())
            {
                if (!UniqueObject(row) || row.EnumerateObject().Any(item => item.Name is not ("fieldKey" or "sourceFieldKey" or "value")) ||
                    !ReadFieldKey(row, "fieldKey", out var key) || !targets.Add(key!)) return false;
                var hasValue = row.TryGetProperty("value", out var constant);
                var hasSource = row.TryGetProperty("sourceFieldKey", out _);
                if (hasValue == hasSource) return false;
                if (hasSource)
                {
                    if (!ReadFieldKey(row, "sourceFieldKey", out var source)) return false;
                    result.Add(new(key!, source, null));
                }
                else
                {
                    if (Encoding.UTF8.GetByteCount(constant.GetRawText()) > 16384) return false;
                    result.Add(new(key!, null, constant.Clone()));
                }
            }
        }
        foreach (var item in result)
        {
            if (!granted.TryGetValue(item.FieldKey, out var targetAccess) || targetAccess is not ("editable" or "required")) return false;
            if (item.SourceFieldKey is { } source && (!granted.TryGetValue(source, out var sourceAccess) || sourceAccess == "hidden")) return false;
            if (fields is null) continue;
            if (!fields.TryGetValue(item.FieldKey, out var target)) return false;
            if (item.SourceFieldKey is { } sourceKey)
            {
                if (!fields.TryGetValue(sourceKey, out var sourceField) || target.FieldTypeKey != sourceField.FieldTypeKey) return false;
            }
            else if (!ValidValue(target, item.Constant!.Value, targetAccess)) return false;
        }
        WorkflowExclusiveGatewayCondition? parsedCondition = null;
        if (value.TryGetProperty("condition", out var expression) &&
            (!WorkflowExclusiveGatewayConfiguration.TryReadCondition(expression, fields, out parsedCondition) ||
             ConditionFields(parsedCondition!).Any(key => !granted.TryGetValue(key, out var mode) || mode == "hidden"))) return false;
        policy = new(result, granted, schema, parsedCondition);
        return true;
    }

    /// <summary>全部右值取自同一个不可变输入；一次动作中的先后顺序不能改变复制来源。</summary>
    public bool TryApply(IReadOnlyDictionary<string, JsonElement> input,
        out WorkflowFieldMutationOutput? output)
    {
        output = null;
        if (schema is null || !WorkflowFormValueValidator.Validate(schema, input)) return false;
        var values = input.ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);
        if (condition is not null)
        {
            if (!condition.TryEvaluate(input, out var matches)) return false;
            if (!matches) { output = new(values, new Dictionary<string, JsonElement>(), false); return true; }
        }
        var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var patch = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in assignments)
        {
            var value = item.SourceFieldKey is { } source
                ? input.TryGetValue(source, out var existing) ? existing : NullValue()
                : item.Constant!.Value;
            if (!ValidValue(fields[item.FieldKey], value, permissions[item.FieldKey])) return false;
            patch.Add(item.FieldKey, value.Clone()); values[item.FieldKey] = value.Clone();
        }
        if (!WorkflowFormValueValidator.Validate(schema, values) || permissions.Where(item => item.Value == "required")
                .Any(item => !values.TryGetValue(item.Key, out var required) || required.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) ||
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(values, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement)) > 131072) return false;
        output = new(values, patch, true);
        return true;
    }

    private static bool ValidValue(WorkflowFormField field, JsonElement value, string permission) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? !field.Required && permission != "required"
            : WorkflowFormValueValidator.IsFieldValueValid(field, value);
    private static IEnumerable<string> ConditionFields(WorkflowExclusiveGatewayCondition value) =>
        value.LogicalOperator is null ? [value.FieldKey] : value.Conditions.SelectMany(ConditionFields);
    private static bool UniqueObject(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();
    private static bool ValidFieldKey(string? key) => key?.Length <= 64 && WorkflowNodeKeyValidator.IsValid(key) && key is not ("__proto__" or "prototype" or "constructor");
    private static bool ReadFieldKey(JsonElement value, string property, out string? key)
    {
        key = null;
        if (!value.TryGetProperty(property, out var field) || field.ValueKind != JsonValueKind.String || !ValidFieldKey(field.GetString())) return false;
        key = field.GetString(); return true;
    }
    private static JsonElement NullValue() { using var value = JsonDocument.Parse("null"); return value.RootElement.Clone(); }
}

/// <summary>受控赋值只含目标字段及常量或同快照来源，两种右值互斥。</summary>
internal sealed record WorkflowFieldAssignment(string FieldKey, string? SourceFieldKey, JsonElement? Constant);
/// <summary>纯求值结果不代表已经落库，后续必须与输入修订、结果回执及推进在同一事务接纳。</summary>
internal sealed record WorkflowFieldMutationOutput(IReadOnlyDictionary<string, JsonElement> Values,
    IReadOnlyDictionary<string, JsonElement> Patch, bool Matched);
