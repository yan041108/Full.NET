using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>按不可变表单协议校验字段集合，禁止未知键和客户端类型漂移。</summary>
internal static class WorkflowFormValueValidator
{
    public static bool Validate(WorkflowFormSchema schema, JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return Validate(schema, values.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal));
    }

    public static bool Validate(
        WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var fields = schema.Sections.SelectMany(section => section.Fields)
            .ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        if (values.Keys.Any(key => !fields.ContainsKey(key)))
        {
            return false;
        }

        foreach (var field in fields.Values)
        {
            if (!values.TryGetValue(field.FieldKey, out var value))
            {
                if (field.Required)
                {
                    return false;
                }

                continue;
            }

            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (field.Required)
                {
                    return false;
                }

                continue;
            }

            if (!IsTypeValid(field.FieldTypeKey, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTypeValid(string fieldTypeKey, JsonElement value) => fieldTypeKey switch
    {
        "text" or "textarea" or "money" or "decimal" or "date" or "time" or "datetime" or
            "radio" or "select" => value.ValueKind == JsonValueKind.String &&
                                   !string.IsNullOrWhiteSpace(value.GetString()),
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "checkbox" => value.ValueKind == JsonValueKind.Array,
        "switch" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false,
    };
}
