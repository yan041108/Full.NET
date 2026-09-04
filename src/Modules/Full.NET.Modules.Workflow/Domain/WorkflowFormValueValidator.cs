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

            if (!IsFieldValueValid(field, value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>校验单个字段值是否符合已发布表单字段的类型与约束。</summary>
    /// <param name="field">已发布的表单字段定义。</param>
    /// <param name="value">待校验的字段值。</param>
    /// <returns>字段值符合声明式协议时返回 <see langword="true"/>。</returns>
    internal static bool IsFieldValueValid(WorkflowFormField field, JsonElement value) =>
        field.FieldTypeKey switch
    {
        "text" or "textarea" => IsTextValid(field, value),
        "money" or "decimal" => IsDecimalValid(field, value),
        "date" or "time" or "datetime" => IsTemporalValid(field, value),
        "radio" or "select" => IsDeclaredOption(field, value),
        "integer" => IsIntegerValid(field, value),
        "checkbox" => AreDeclaredOptions(field, value),
        "switch" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false,
    };

    private static bool IsTextValid(WorkflowFormField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) ||
            !WorkflowFormFieldConstraints.TryReadTextLength(
                field,
                out var minimumLength,
                out var maximumLength))
        {
            return false;
        }

        var length = value.GetString()!.Length;
        return length >= minimumLength && length <= maximumLength;
    }

    private static bool IsIntegerValid(WorkflowFormField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var integer) ||
            !WorkflowFormFieldConstraints.TryReadIntegerRange(field, out var minimum, out var maximum))
        {
            return false;
        }

        return integer >= minimum && integer <= maximum;
    }

    private static bool IsDecimalValid(WorkflowFormField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String ||
            !WorkflowFormFieldConstraints.TryReadDecimalConstraints(
                field,
                field.FieldTypeKey == "money" ? 4 : 28,
                out var scale,
                out var minimum,
                out var maximum) ||
            !WorkflowFormFieldConstraints.TryParseCanonicalDecimal(
                value.GetString()!,
                scale,
                out var number))
        {
            return false;
        }

        return number >= minimum && number <= maximum;
    }

    private static bool IsTemporalValid(WorkflowFormField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String ||
            !WorkflowFormFieldConstraints.TryReadTemporalRange(field, out var minimum, out var maximum) ||
            !WorkflowFormFieldConstraints.TryParseCanonicalTemporal(
                field.FieldTypeKey,
                value.GetString()!,
                out var temporalValue))
        {
            return false;
        }

        return temporalValue >= minimum && temporalValue <= maximum;
    }

    private static bool IsDeclaredOption(WorkflowFormField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String ||
            !WorkflowFormChoiceOptions.TryRead(field, out var options))
        {
            return false;
        }

        return options.Contains(value.GetString()!, StringComparer.Ordinal);
    }

    private static bool AreDeclaredOptions(WorkflowFormField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array ||
            !WorkflowFormChoiceOptions.TryRead(field, out var options))
        {
            return false;
        }

        var selected = value.EnumerateArray().ToArray();
        if ((field.Required && selected.Length == 0) ||
            selected.Any(item => item.ValueKind != JsonValueKind.String))
        {
            return false;
        }

        var selectedKeys = selected.Select(item => item.GetString()!).ToArray();
        return selectedKeys.Distinct(StringComparer.Ordinal).Count() == selectedKeys.Length &&
               selectedKeys.All(key => options.Contains(key, StringComparer.Ordinal));
    }

}
