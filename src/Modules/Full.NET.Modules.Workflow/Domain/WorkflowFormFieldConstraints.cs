using System.Globalization;
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

    public static bool TryReadDecimalConstraints(
        WorkflowFormField field,
        int maximumAllowedScale,
        out int scale,
        out decimal minimum,
        out decimal maximum)
    {
        scale = 0;
        minimum = decimal.MinValue;
        maximum = decimal.MaxValue;

        return TryReadDecimalScale(field, maximumAllowedScale, out scale) &&
               TryReadOptionalDecimal(field, "minimum", scale, ref minimum) &&
               TryReadOptionalDecimal(field, "maximum", scale, ref maximum) &&
               maximum >= minimum;
    }

    public static bool TryReadDecimalScale(
        WorkflowFormField field,
        int maximumAllowedScale,
        out int scale)
    {
        scale = 0;
        return field.Constraints.TryGetValue("scale", out var configured) &&
               configured.ValueKind == JsonValueKind.Number &&
               configured.TryGetInt32(out scale) &&
               scale >= 0 &&
               scale <= maximumAllowedScale;
    }

    public static bool TryParseCanonicalDecimal(
        string text,
        int maximumScale,
        out decimal value)
    {
        value = default;
        if (string.IsNullOrEmpty(text) || text[0] == '+' || text.Trim() != text)
        {
            return false;
        }

        var digitStart = text[0] == '-' ? 1 : 0;
        if (digitStart == text.Length)
        {
            return false;
        }

        var decimalPoint = text.IndexOf('.', digitStart);
        if (decimalPoint >= 0 && text.IndexOf('.', decimalPoint + 1) >= 0)
        {
            return false;
        }

        var integerEnd = decimalPoint >= 0 ? decimalPoint : text.Length;
        var integerLength = integerEnd - digitStart;
        var fractionLength = decimalPoint >= 0 ? text.Length - decimalPoint - 1 : 0;
        if (integerLength == 0 ||
            fractionLength > maximumScale ||
            (decimalPoint >= 0 && fractionLength == 0) ||
            (integerLength > 1 && text[digitStart] == '0') ||
            !ContainsOnlyDigits(text.AsSpan(digitStart, integerLength)) ||
            (fractionLength > 0 && !ContainsOnlyDigits(text.AsSpan(decimalPoint + 1))))
        {
            return false;
        }

        if (!decimal.TryParse(
                text,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value))
        {
            return false;
        }

        return value != decimal.Zero || text[0] != '-';
    }

    public static bool TryReadTemporalRange(
        WorkflowFormField field,
        out long minimum,
        out long maximum)
    {
        minimum = long.MinValue;
        maximum = long.MaxValue;

        return TryReadOptionalTemporal(field, "minimum", ref minimum) &&
               TryReadOptionalTemporal(field, "maximum", ref maximum) &&
               maximum >= minimum;
    }

    public static bool TryParseCanonicalTemporal(
        string fieldTypeKey,
        string text,
        out long sortableValue)
    {
        sortableValue = default;
        switch (fieldTypeKey)
        {
            case "date" when DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date):
                sortableValue = date.DayNumber;
                return true;
            case "time" when TimeOnly.TryParseExact(
                text,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time):
                sortableValue = time.Ticks;
                return true;
            case "datetime":
                return TryParseCanonicalDateTime(text, out sortableValue);
            default:
                return false;
        }
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

    private static bool TryReadOptionalDecimal(
        WorkflowFormField field,
        string constraintKey,
        int scale,
        ref decimal value)
    {
        if (!field.Constraints.TryGetValue(constraintKey, out var configured))
        {
            return true;
        }

        var text = configured.ValueKind switch
        {
            JsonValueKind.Number => configured.GetRawText(),
            JsonValueKind.String => configured.GetString(),
            _ => null,
        };

        return text is not null && TryParseCanonicalDecimal(text, scale, out value);
    }

    private static bool TryReadOptionalTemporal(
        WorkflowFormField field,
        string constraintKey,
        ref long value)
    {
        if (!field.Constraints.TryGetValue(constraintKey, out var configured))
        {
            return true;
        }

        return configured.ValueKind == JsonValueKind.String &&
               TryParseCanonicalTemporal(field.FieldTypeKey, configured.GetString()!, out value);
    }

    private static bool TryParseCanonicalDateTime(string text, out long utcTicks)
    {
        utcTicks = default;
        DateTimeOffset parsed;
        if (text.EndsWith('Z'))
        {
            if (!DateTimeOffset.TryParseExact(
                    text,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
            {
                return false;
            }
        }
        else if (!DateTimeOffset.TryParseExact(
                     text,
                     "yyyy-MM-dd'T'HH:mm:sszzz",
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.None,
                     out parsed))
        {
            return false;
        }

        utcTicks = parsed.UtcDateTime.Ticks;
        return true;
    }

    private static bool ContainsOnlyDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
