using System.Net;
using System.Text.RegularExpressions;
using System.Text;

namespace Full.NET.Modules.Printing.Domain;

/// <summary>渲染布局与已编码字段；布局仍属不可信 HTML，消费端必须在插入 DOM 前执行白名单净化。</summary>
internal static partial class PrintingHtmlRenderer
{
    [GeneratedRegex(@"\{\{\s*(?<key>[a-zA-Z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    /// <summary>移除脚本标签并替换占位符为 HTML 编码后的字段值。</summary>
    public static string Render(
        string layoutHtml,
        IReadOnlyDictionary<string, string?> boundFields)
    {
        ArgumentNullException.ThrowIfNull(boundFields);
        var sanitized = StripScripts(layoutHtml ?? string.Empty);
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
        StripScripts(layoutHtml ?? string.Empty);

    private static string StripScripts(string html)
    {
        var output = new StringBuilder(html.Length);
        var offset = 0;
        while (offset < html.Length)
        {
            var start = html.IndexOf("<script", offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                output.Append(html.AsSpan(offset));
                break;
            }
            var boundary = start + 7;
            if (boundary < html.Length && (char.IsLetterOrDigit(html[boundary]) || html[boundary] == '_'))
            {
                output.Append(html.AsSpan(offset, boundary - offset));
                offset = boundary;
                continue;
            }
            output.Append(html.AsSpan(offset, start - offset));
            // 未闭合脚本丢弃剩余内容；每个字符只参与固定次数扫描，消费端仍需白名单净化。
            var end = html.IndexOf("</script>", boundary, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;
            offset = end + 9;
        }
        return output.ToString();
    }
}
