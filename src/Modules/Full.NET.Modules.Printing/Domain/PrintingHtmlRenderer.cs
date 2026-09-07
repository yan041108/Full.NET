using System.Net;
using System.Text.RegularExpressions;

namespace Full.NET.Modules.Printing.Domain;

/// <summary>渲染布局与已编码字段；布局仍属不可信 HTML，消费端必须在插入 DOM 前执行白名单净化。</summary>
internal static partial class PrintingHtmlRenderer
{
    [GeneratedRegex(@"\{\{\s*(?<key>[a-zA-Z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    private static readonly Regex ScriptTagRegex = new(
        "<script\\b[^<]*(?:(?!<\\/script>)<[^<]*)*<\\/script>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>移除脚本标签并替换占位符为 HTML 编码后的字段值。</summary>
    public static string Render(
        string layoutHtml,
        IReadOnlyDictionary<string, string?> boundFields)
    {
        ArgumentNullException.ThrowIfNull(boundFields);
        var sanitized = ScriptTagRegex.Replace(layoutHtml ?? string.Empty, string.Empty);
        return PlaceholderRegex().Replace(
            sanitized,
            match =>
            {
                var key = match.Groups["key"].Value;
                if (!boundFields.TryGetValue(key, out var value))
                {
                    return string.Empty;
                }

                return WebUtility.HtmlEncode(value ?? string.Empty);
            });
    }

    /// <summary>保存前剥离脚本标签；此预处理不是完整的安全净化，不能替代消费端 HTML 白名单。</summary>
    /// <param name="layoutHtml">用户编辑的原始布局。</param>
    /// <returns>仍需在消费边界净化的布局片段。</returns>
    public static string SanitizeLayoutHtml(string layoutHtml) =>
        ScriptTagRegex.Replace(layoutHtml ?? string.Empty, string.Empty);
}
