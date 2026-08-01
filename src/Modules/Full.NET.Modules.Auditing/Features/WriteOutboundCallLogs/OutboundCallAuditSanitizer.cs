using System.Text.RegularExpressions;

namespace Full.NET.Modules.Auditing.Features.WriteOutboundCallLogs;

/// <summary>出站审计写入前的键名、错误码与目标类别脱敏。</summary>
internal static partial class OutboundCallAuditSanitizer
{
    private const string Redacted = "[redacted]";

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableKeyPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceIdPattern();

    [GeneratedRegex(
        @"(?i)(authorization\s*[:=]|bearer\s+|api[_-]?key\s*[:=]|password\s*[:=]|cookie\s*[:=]|set-cookie\s*[:=]|connection\s*string\s*[:=]|secret\s*[:=]|token\s*[:=])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretMarkerPattern();

    [GeneratedRegex(
        @"(?i)(Server\s*=\s*[^;]+;.*Password\s*=|User\s+Id\s*=\s*[^;]+;.*Password\s*=)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringPattern();

    public static string SanitizeProviderKey(string value) =>
        SanitizeStableKey(value, 64, "provider.unknown");

    public static string SanitizeOperationKey(string value) =>
        SanitizeStableKey(value, 128, "operation.unknown");

    public static string SanitizeDestinationHostCategory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "host.unknown";
        }

        var trimmed = value.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            var host = absolute.Host;
            return ContainsSensitiveContent(host)
                ? "host.redacted"
                : SanitizeStableKey(host, 64, "host.unknown");
        }

        var withoutQuery = trimmed.Split('?', '#')[0];
        if (ContainsSensitiveContent(withoutQuery))
        {
            return "host.redacted";
        }

        var hostOnly = withoutQuery
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? withoutQuery;
        return SanitizeStableKey(hostOnly, 64, "host.unknown");
    }

    public static string? SanitizeSafeErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (ContainsSensitiveContent(trimmed) || trimmed.Contains('\n', StringComparison.Ordinal))
        {
            return "error.redacted";
        }

        return SanitizeStableKey(trimmed, 128, "error.redacted");
    }

    public static string? SanitizeTraceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 64 || !TraceIdPattern().IsMatch(trimmed) || ContainsSensitiveContent(trimmed))
        {
            return null;
        }

        return trimmed;
    }

    public static bool ContainsSensitiveContent(string value) =>
        SecretMarkerPattern().IsMatch(value)
        || ConnectionStringPattern().IsMatch(value);

    private static string SanitizeStableKey(
        string value,
        int maxLength,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || ContainsSensitiveContent(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > maxLength)
        {
            normalized = normalized[..maxLength];
        }

        return StableKeyPattern().IsMatch(normalized)
            ? normalized
            : fallback;
    }
}
