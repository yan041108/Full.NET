using System.Text.Json;
using System.Text.RegularExpressions;

namespace Full.NET.Abstractions.Auditing;

/// <summary>把模块域审计 JSON 摘要解析为脱敏字段差异。</summary>
public static partial class DomainAuditChangeDiffParser
{
    private const int MaxFieldValueLength = 256;

    [GeneratedRegex(
        @"(?i)(password|secret|token|authorization|cookie|connectionstring|api[_-]?key)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveFieldKeyPattern();

    [GeneratedRegex(
        @"(?i)(authorization\s*[:=]|bearer\s+|api[_-]?key\s*[:=]|password\s*[:=]|cookie\s*[:=]|secret\s*[:=]|token\s*[:=])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretMarkerPattern();

    /// <summary>判断差异摘要的可读性状态。</summary>
    public static DomainAuditChangeDiffAvailability ParseAvailability(string? diffSummaryJson)
    {
        if (string.IsNullOrWhiteSpace(diffSummaryJson))
        {
            return DomainAuditChangeDiffAvailability.NoDiffRecorded;
        }

        try
        {
            using var document = JsonDocument.Parse(diffSummaryJson);
            return ExtractFields(document.RootElement).Count > 0
                ? DomainAuditChangeDiffAvailability.Available
                : DomainAuditChangeDiffAvailability.NoDiffRecorded;
        }
        catch (JsonException)
        {
            return DomainAuditChangeDiffAvailability.Unparseable;
        }
    }

    /// <summary>解析并脱敏字段差异列表。</summary>
    public static IReadOnlyList<DomainAuditChangeDiffField> ParseFields(string? diffSummaryJson)
    {
        if (string.IsNullOrWhiteSpace(diffSummaryJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(diffSummaryJson);
            return ExtractFields(document.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<DomainAuditChangeDiffField> ExtractFields(JsonElement root)
    {
        var fields = new List<DomainAuditChangeDiffField>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return fields;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (TryExtractField(property.Name, property.Value, out var field))
            {
                fields.Add(field);
            }
        }

        return fields;
    }

    private static bool TryExtractField(
        string fieldKey,
        JsonElement value,
        out DomainAuditChangeDiffField field)
    {
        var sanitizedKey = SanitizeFieldKey(fieldKey);
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("before", out var beforeElement)
                || value.TryGetProperty("old", out beforeElement))
            {
                var hasAfter = value.TryGetProperty("after", out var afterElement)
                    || value.TryGetProperty("new", out afterElement);
                field = new DomainAuditChangeDiffField(
                    sanitizedKey,
                    SanitizeFieldValue(FormatJsonValue(beforeElement)),
                    hasAfter
                        ? SanitizeFieldValue(FormatJsonValue(afterElement))
                        : null);
                return true;
            }

            if (value.TryGetProperty("after", out var afterOnlyElement)
                || value.TryGetProperty("new", out afterOnlyElement))
            {
                field = new DomainAuditChangeDiffField(
                    sanitizedKey,
                    null,
                    SanitizeFieldValue(FormatJsonValue(afterOnlyElement)));
                return true;
            }
        }

        field = new DomainAuditChangeDiffField(
            sanitizedKey,
            null,
            SanitizeFieldValue(FormatJsonValue(value)));
        return true;
    }

    private static string SanitizeFieldKey(string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            return "field.unknown";
        }

        var normalized = fieldKey.Trim().ToLowerInvariant();
        if (normalized.Length > 64)
        {
            normalized = normalized[..64];
        }

        return SensitiveFieldKeyPattern().IsMatch(normalized)
            ? "field.redacted"
            : normalized;
    }

    private static string? SanitizeFieldValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (SecretMarkerPattern().IsMatch(value))
        {
            return "[redacted]";
        }

        var trimmed = value.Trim();
        return trimmed.Length > MaxFieldValueLength
            ? trimmed[..MaxFieldValueLength]
            : trimmed;
    }

    private static string? FormatJsonValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            _ => element.GetRawText(),
        };
}
