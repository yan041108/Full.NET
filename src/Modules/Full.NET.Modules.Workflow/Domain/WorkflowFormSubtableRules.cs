using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>明细子表列定义，供发布编译与运行时单元格校验共用。</summary>
internal sealed record WorkflowFormSubtableColumn(
    string ColumnKey,
    string FieldTypeKey,
    bool Required,
    IReadOnlyDictionary<string, JsonElement> Constraints)
{
    /// <summary>将列定义投影为与顶层字段相同的校验形状。</summary>
    public WorkflowFormField ToField() =>
        new(ColumnKey, FieldTypeKey, Required, Constraints);
}

/// <summary>解析明细子表声明式约束，并校验列目录闭合且无嵌套子表。</summary>
internal static class WorkflowFormSubtableConstraints
{
    public const int MinimumMaxRows = 1;
    public const int MaximumMaxRows = 20;
    public const int MinimumColumns = 1;
    public const int MaximumColumns = 16;

    private static readonly HashSet<string> AllowedColumnFieldTypes =
        new(StringComparer.Ordinal)
        {
            "text", "textarea", "integer", "decimal", "money", "date", "time", "datetime",
            "radio", "checkbox", "select", "switch",
        };

    private static readonly HashSet<string> ChoiceColumnFieldTypes =
        new(StringComparer.Ordinal) { "radio", "checkbox", "select" };

    public static bool TryRead(
        WorkflowFormField field,
        out int maxRows,
        out IReadOnlyList<WorkflowFormSubtableColumn> columns)
    {
        maxRows = 0;
        columns = [];

        if (field.FieldTypeKey != "subtable" ||
            !field.Constraints.TryGetValue("maxRows", out var maxRowsElement) ||
            maxRowsElement.ValueKind != JsonValueKind.Number ||
            !maxRowsElement.TryGetInt32(out maxRows) ||
            maxRows < MinimumMaxRows ||
            maxRows > MaximumMaxRows ||
            !field.Constraints.TryGetValue("columns", out var columnsElement) ||
            columnsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var length = columnsElement.GetArrayLength();
        if (length < MinimumColumns || length > MaximumColumns)
        {
            return false;
        }

        var parsed = new List<WorkflowFormSubtableColumn>();
        var columnKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in columnsElement.EnumerateArray())
        {
            if (!TryReadColumn(item, out var column) || !columnKeys.Add(column!.ColumnKey))
            {
                return false;
            }

            parsed.Add(column!);
        }

        columns = parsed;
        return true;
    }

    private static bool TryReadColumn(JsonElement element, out WorkflowFormSubtableColumn? column)
    {
        column = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("columnKey", out var columnKeyElement) ||
            columnKeyElement.ValueKind != JsonValueKind.String ||
            !IsStableKey(columnKeyElement.GetString()) ||
            !element.TryGetProperty("fieldTypeKey", out var fieldTypeElement) ||
            fieldTypeElement.ValueKind != JsonValueKind.String ||
            !AllowedColumnFieldTypes.Contains(fieldTypeElement.GetString()!) ||
            !element.TryGetProperty("required", out var requiredElement) ||
            requiredElement.ValueKind != JsonValueKind.True && requiredElement.ValueKind != JsonValueKind.False ||
            !element.TryGetProperty("constraints", out var constraintsElement) ||
            constraintsElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var constraints = constraintsElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var synthetic = new WorkflowFormField(
            columnKeyElement.GetString()!,
            fieldTypeElement.GetString()!,
            requiredElement.GetBoolean(),
            constraints);
        if (!WorkflowFormComponentCatalog.TryGet(synthetic.FieldTypeKey, out var component) ||
            !component!.Publishable ||
            !component.Executable ||
            constraints.Keys.Any(key => !component.SupportsConstraint(key)) ||
            !IsColumnConstraintShapeValid(synthetic))
        {
            return false;
        }

        column = new WorkflowFormSubtableColumn(
            synthetic.FieldKey,
            synthetic.FieldTypeKey,
            synthetic.Required,
            constraints);
        return true;
    }

    private static bool IsColumnConstraintShapeValid(WorkflowFormField column)
    {
        if (ChoiceColumnFieldTypes.Contains(column.FieldTypeKey) &&
            !WorkflowFormChoiceOptions.TryRead(column, out _))
        {
            return false;
        }

        return column.FieldTypeKey switch
        {
            "text" or "textarea" => WorkflowFormFieldConstraints.TryReadTextLength(column, out _, out _),
            "integer" => WorkflowFormFieldConstraints.TryReadIntegerRange(column, out _, out _),
            "decimal" => WorkflowFormFieldConstraints.TryReadDecimalConstraints(column, 28, out _, out _, out _),
            "money" => WorkflowFormFieldConstraints.TryReadDecimalConstraints(column, 4, out _, out _, out _)
                && WorkflowFormFieldConstraints.TryReadDecimalScale(column, 4, out _),
            "date" or "time" or "datetime" => WorkflowFormFieldConstraints.TryReadTemporalRange(
                column,
                out _,
                out _),
            "switch" => true,
            _ => false,
        };
    }

    private static bool IsStableKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 ||
            value.Equals("__proto__", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("prototype", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("constructor", StringComparison.OrdinalIgnoreCase) ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        foreach (var character in value.AsSpan(1))
        {
            if (!IsAsciiLetter(character) && !char.IsAsciiDigit(character) && character is not ('_' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}

/// <summary>校验明细子表运行时值：行数上限、列闭合与单元格类型。</summary>
internal static class WorkflowFormSubtableValueRules
{
    public static bool IsValid(WorkflowFormField field, JsonElement value)
    {
        if (!WorkflowFormSubtableConstraints.TryRead(field, out var maxRows, out var columns))
        {
            return false;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var rows = value.EnumerateArray().ToArray();
        if (rows.Length > maxRows || (field.Required && rows.Length == 0))
        {
            return false;
        }

        var columnMap = columns.ToDictionary(column => column.ColumnKey, StringComparer.Ordinal);
        return rows.All(row => IsRowValid(row, columnMap));
    }

    private static bool IsRowValid(
        JsonElement row,
        IReadOnlyDictionary<string, WorkflowFormSubtableColumn> columns)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var properties = row.EnumerateObject().ToArray();
        if (properties.Any(property => !columns.ContainsKey(property.Name)) ||
            properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
        {
            return false;
        }

        foreach (var column in columns.Values)
        {
            var property = properties.FirstOrDefault(item => item.Name == column.ColumnKey);
            if (property.Name is null)
            {
                if (column.Required)
                {
                    return false;
                }

                continue;
            }

            var cell = property.Value;
            if (cell.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (column.Required)
                {
                    return false;
                }

                continue;
            }

            if (!WorkflowFormValueValidator.IsFieldValueValid(column.ToField(), cell))
            {
                return false;
            }
        }

        return true;
    }
}
