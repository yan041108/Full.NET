namespace Full.NET.Modules.Ai.Domain;

/// <summary>未知文本不是可信摘要；静态工具提供字段白名单之前仅记录省略标记。</summary>
internal static class AiAgentToolAuditPolicy
{
    public const int MaxSummaryLength = 512;

    /// <summary>拒绝保留任意原始参数或输出，嵌套 JSON 与自由文本均不能依靠正则保证脱敏。</summary>
    /// <param name="value">未经工具字段白名单处理的原始内容。</param>
    public static string Summarize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : "[redacted]";
}
