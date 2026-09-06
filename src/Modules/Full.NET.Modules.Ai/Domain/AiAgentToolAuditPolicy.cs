using System.Text.RegularExpressions;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>Agent Tool 调用审计摘要与脱敏策略。</summary>
internal static partial class AiAgentToolAuditPolicy
{
    public const int MaxSummaryLength = 512;

    [GeneratedRegex(@"(?i)(api[_-]?key|token|password|secret)\s*[:=]\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9]{8,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyPattern();

    /// <summary>将原始文本压缩为可写入审计表的摘要。</summary>
    /// <param name="value">原始文本。</param>
    public static string Summarize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = SecretPattern().Replace(value, "[redacted]");
        sanitized = ApiKeyPattern().Replace(sanitized, "[redacted]");
        sanitized = sanitized.Trim();
        if (sanitized.Length <= MaxSummaryLength)
        {
            return sanitized;
        }

        return sanitized[..(MaxSummaryLength - 3)] + "...";
    }
}
