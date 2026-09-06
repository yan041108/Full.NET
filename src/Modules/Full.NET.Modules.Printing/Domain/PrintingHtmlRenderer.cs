using System.Net;
using System.Text.RegularExpressions;

namespace Full.NET.Modules.Printing.Domain;

/// <summary>将布局 HTML 与绑定字段渲染为可安全预览的 HTML 片段。</summary>
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

    /// <summary>保存前剥离脚本标签，降低模板注入风险。</summary>
    public static string SanitizeLayoutHtml(string layoutHtml) =>
        ScriptTagRegex.Replace(layoutHtml ?? string.Empty, string.Empty);
}
