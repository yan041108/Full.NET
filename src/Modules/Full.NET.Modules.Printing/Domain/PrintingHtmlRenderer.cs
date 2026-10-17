using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.Modules.Printing.Domain;

/// <summary>渲染布局与已编码字段；布局仍属不可信 HTML，消费端必须在插入 DOM 前执行白名单净化。</summary>
internal static partial class PrintingHtmlRenderer
{
    [GeneratedRegex(@"\{\{\s*(?<key>[a-zA-Z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant, 250)]
    private static partial Regex PlaceholderRegex();

    private static readonly Regex ScriptTagRegex = new(
        "<script\\b[^<]*(?:(?!<\\/script>)<[^<]*)*<\\/script>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>移除脚本标签并替换占位符为 HTML 编码后的字段值。</summary>
    /// <param name="layoutHtml">受 64 Ki 字符上限约束的原始布局，消费端仍须净化。</param>
    /// <param name="boundFields">已经投影的 Schema 字段；编码前再核对文本预算。</param>
    /// <param name="cancellationToken">扫描与输出构造的取消令牌。</param>
    public static string Render(
        string layoutHtml,
        IReadOnlyDictionary<string, string?> boundFields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boundFields);
        cancellationToken.ThrowIfCancellationRequested();
        var budget = new PrintingRenderingBudget();
        foreach (var field in boundFields) budget.AddField(field.Key, field.Value, cancellationToken);
        var sanitized = SanitizeLayoutHtml(layoutHtml, cancellationToken);
        var output = new StringBuilder(sanitized.Length);
        var encoded = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        try
        {
            foreach (Match match in PlaceholderRegex().Matches(sanitized))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Append(output, sanitized.AsSpan(position, match.Index - position), cancellationToken);
                var key = match.Groups["key"].Value;
                if (!encoded.TryGetValue(key, out var text))
                {
                    text = boundFields.TryGetValue(key, out var value) ? WebUtility.HtmlEncode(value ?? string.Empty) : string.Empty;
                    encoded.Add(key, text);
                }
                Append(output, text.AsSpan(), cancellationToken);
                position = match.Index + match.Length;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Regex 超时异常保留完整输入；只传稳定机器码，避免保存业务布局原文。
            throw new PrintingRenderingLimitException(PrintingErrorCodes.LayoutHtmlInvalid);
        }
        Append(output, sanitized.AsSpan(position), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return output.ToString();
    }

    /// <summary>保存前剥离脚本标签；此预处理不是完整的安全净化，不能替代消费端 HTML 白名单。</summary>
    /// <param name="layoutHtml">用户编辑的原始布局。</param>
    /// <param name="cancellationToken">预处理前后的取消令牌。</param>
    /// <returns>仍需在消费边界净化的布局片段。</returns>
    public static string SanitizeLayoutHtml(string layoutHtml, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        layoutHtml ??= string.Empty;
        if (layoutHtml.Length > PrintingLayoutPolicy.MaxLayoutHtmlLength)
            throw new PrintingRenderingLimitException(PrintingErrorCodes.LayoutHtmlInvalid);
        try
        {
            var result = ScriptTagRegex.Replace(layoutHtml, string.Empty);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new PrintingRenderingLimitException(PrintingErrorCodes.LayoutHtmlInvalid);
        }
    }

    /// <summary>在增长输出缓冲之前限制编码后长度；保留完整结果或明确失败。</summary>
    /// <param name="output">当前有界输出缓冲。</param>
    /// <param name="text">即将追加的字面量或已编码字段。</param>
    /// <param name="token">当前取消令牌。</param>
    private static void Append(StringBuilder output, ReadOnlySpan<char> text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if ((long)output.Length + text.Length > PrintingLayoutPolicy.MaxRenderedHtmlLength)
            throw new PrintingRenderingLimitException(PrintingErrorCodes.LayoutHtmlInvalid);
        output.Append(text);
    }
}
