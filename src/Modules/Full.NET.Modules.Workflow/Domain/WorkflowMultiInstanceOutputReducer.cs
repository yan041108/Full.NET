using System.Globalization;
using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>只归约已固化成功集合的显式父字段补丁；顺序使用ItemKey，数值沿用现行整数与定点字符串契约。</summary>
internal static class WorkflowMultiInstanceOutputReducer
{
    public static bool Supports(string type, string rule) => rule is "first" or "last" ||
        type is "integer" or "decimal" or "money" && rule is "sum" or "min" or "max" ||
        type == "switch" && rule is "all_true" or "any_true" || type is "attachment" or "subtable" && rule == "append";
    public static bool TryMerge(WorkflowFormSchema schema, IReadOnlyCollection<string> outputFields,
        IReadOnlyDictionary<string, string> reducers, IReadOnlyList<WorkflowMultiInstanceOutput> outputs,
        out IReadOnlyDictionary<string, JsonElement> patch)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal); patch = result;
        var declared = outputFields.ToHashSet(StringComparer.Ordinal);
        if (outputs.Count is < 1 or > WorkflowMultiInstancePolicy.MaximumItems || declared.Count != outputFields.Count ||
            declared.Count > WorkflowSubprocessPolicy.MaximumMappings || !declared.SetEquals(reducers.Keys) ||
            outputs.Any(item => item.ItemId == Guid.Empty || !WorkflowSubprocessPolicy.Key(item.ItemKey) || item.Values is null || !declared.SetEquals(item.Values.Keys)) ||
            outputs.Select(item => item.ItemId).Distinct().Count() != outputs.Count || outputs.Select(item => item.ItemKey).Distinct(StringComparer.Ordinal).Count() != outputs.Count) return false;
        var fields = schema.Sections.SelectMany(section => section.Fields).ToArray();
        if (fields.Select(field => field.FieldKey).Distinct(StringComparer.Ordinal).Count() != fields.Length) return false;
        var byKey = fields.ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var ordered = outputs.OrderBy(item => item.ItemKey, StringComparer.Ordinal).ToArray();
        foreach (var key in declared.Order(StringComparer.Ordinal))
        {
            if (!byKey.TryGetValue(key, out var field) || field.FieldTypeKey is not ("text" or "textarea" or "integer" or "decimal" or "money" or "date" or "time" or "datetime" or "radio" or "select" or "checkbox" or "switch" or "attachment" or "subtable") || !WorkflowSubprocessPolicy.Key(key)) return false;
            var values = ordered.Select(item => item.Values[key]).ToArray();
            if (values.Any(candidate => !Valid(field, candidate)) || !TryReduce(field, reducers[key], values, out var reduced) || !Valid(field, reduced)) return false;
            result.Add(key, reduced.Clone());
        }
        return true;
    }
    private static bool Valid(WorkflowFormField field, JsonElement value) => value.ValueKind != JsonValueKind.Undefined &&
        (value.ValueKind == JsonValueKind.Null ? !field.Required : WorkflowFormValueValidator.IsFieldValueValid(field, value));

    private static bool TryReduce(WorkflowFormField field, string rule, IReadOnlyList<JsonElement> values, out JsonElement result)
    {
        result = default;
        if (rule is "first" or "last") { result = values[rule == "first" ? 0 : values.Count - 1].Clone(); return true; }
        if (rule == "append")
        {
            var maximum = field.FieldTypeKey == "attachment" && WorkflowFormAttachmentConstraints.TryRead(field, out var files, out _, out _) ? files
                : field.FieldTypeKey == "subtable" && WorkflowFormSubtableConstraints.TryRead(field, out var rows, out _) ? rows : -1;
            if (maximum < 0 || values.Any(value => value.ValueKind != JsonValueKind.Array) || values.Sum(value => value.GetArrayLength()) > maximum) return false;
            var json = WorkflowJsonCanonicalizer.Compile(writer => { writer.WriteStartArray();
                foreach (var value in values) foreach (var item in value.EnumerateArray()) item.WriteTo(writer); writer.WriteEndArray(); }).CanonicalJson;
            using var document = JsonDocument.Parse(json); result = document.RootElement.Clone(); return true;
        }
        if (rule is "all_true" or "any_true")
        {
            if (field.FieldTypeKey != "switch" || values.Any(value => value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))) return false;
            result = JsonSerializer.SerializeToElement(rule == "all_true" ? values.All(value => value.GetBoolean()) : values.Any(value => value.GetBoolean()),
                WorkflowJsonSerializerContext.Default.Boolean); return true;
        }
        if (rule is not ("sum" or "min" or "max")) return false;
        try
        {
            if (field.FieldTypeKey == "integer")
            {
                if (values.Any(value => value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out _))) return false;
                var number = values[0].GetInt64();
                foreach (var value in values.Skip(1)) number = rule == "sum" ? checked(number + value.GetInt64()) : rule == "min" ? Math.Min(number, value.GetInt64()) : Math.Max(number, value.GetInt64());
                result = JsonSerializer.SerializeToElement(number, WorkflowJsonSerializerContext.Default.Int64); return true;
            }
            if (field.FieldTypeKey is not ("decimal" or "money") ||
                !WorkflowFormFieldConstraints.TryReadDecimalConstraints(field, field.FieldTypeKey == "money" ? 4 : 28, out var scale, out _, out _)) return false;
            var numbers = new List<decimal>();
            foreach (var value in values)
            {
                if (value.ValueKind != JsonValueKind.String || !WorkflowFormFieldConstraints.TryParseCanonicalDecimal(value.GetString()!, scale, out var number)) return false;
                numbers.Add(number);
            }
            var total = numbers[0];
            foreach (var number in numbers.Skip(1)) total = rule == "sum" ? checked(total + number) : rule == "min" ? Math.Min(total, number) : Math.Max(total, number);
            result = JsonSerializer.SerializeToElement(total.ToString("0.############################", CultureInfo.InvariantCulture), WorkflowJsonSerializerContext.Default.String); return true;
        }
        catch (OverflowException) { return false; }
    }
}

internal sealed record WorkflowMultiInstanceOutput(Guid ItemId, string ItemKey, IReadOnlyDictionary<string, JsonElement> Values);
