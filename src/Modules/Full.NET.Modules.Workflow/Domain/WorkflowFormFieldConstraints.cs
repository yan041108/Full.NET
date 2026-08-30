using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>解析表单字段的声明式约束，确保发布编译与运行时使用相同边界。</summary>
internal static class WorkflowFormFieldConstraints
{
    public static bool TryReadTextLength(
        WorkflowFormField field,
        out int minimumLength,
        out int maximumLength)
    {
        minimumLength = 0;
        maximumLength = int.MaxValue;

        return TryReadOptionalInt32(field, "minLength", ref minimumLength) &&
               TryReadOptionalInt32(field, "maxLength", ref maximumLength) &&
               minimumLength >= 0 &&
               maximumLength >= minimumLength;
    }

    public static bool TryReadIntegerRange(
        WorkflowFormField field,
        out long minimum,
        out long maximum)
    {
        minimum = long.MinValue;
        maximum = long.MaxValue;

        return TryReadOptionalInt64(field, "minimum", ref minimum) &&
               TryReadOptionalInt64(field, "maximum", ref maximum) &&
               maximum >= minimum;
    }

    private static bool TryReadOptionalInt32(
        WorkflowFormField field,
        string constraintKey,
        ref int value)
    {
        if (!field.Constraints.TryGetValue(constraintKey, out var configured))
        {
            return true;
        }

        return configured.ValueKind == JsonValueKind.Number && configured.TryGetInt32(out value);
    }

    private static bool TryReadOptionalInt64(
        WorkflowFormField field,
        string constraintKey,
        ref long value)
    {
        if (!field.Constraints.TryGetValue(constraintKey, out var configured))
        {
            return true;
        }

        return configured.ValueKind == JsonValueKind.Number && configured.TryGetInt64(out value);
    }
}
