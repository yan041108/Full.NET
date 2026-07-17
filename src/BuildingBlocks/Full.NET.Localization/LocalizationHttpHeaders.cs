using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Full.NET.Localization;

/// <summary>
/// 为确定包含本地化文本的 HTTP 响应设置语言协商头。
/// </summary>
public static class LocalizationHttpHeaders
{
    /// <summary>
    /// 设置响应语言，并按需将 Accept-Language 无重复地加入 Vary。
    /// </summary>
    /// <remarks>
    /// 语言中立响应不应调用此方法，避免无意义地降低公共缓存命中率。
    /// </remarks>
    /// <param name="response">将要返回本地化文本的 HTTP 响应。</param>
    /// <param name="locale">响应正文实际使用的规范语言标签。</param>
    /// <param name="varyByAcceptLanguage">响应是否会随 Accept-Language 改变。</param>
    public static void Apply(
        HttpResponse response,
        string locale,
        bool varyByAcceptLanguage)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);

        response.Headers.ContentLanguage = locale;
        if (!varyByAcceptLanguage || ContainsAcceptLanguage(response.Headers.Vary))
        {
            return;
        }

        response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptLanguage);
    }

    private static bool ContainsAcceptLanguage(IEnumerable<string?> headerValues) =>
        headerValues
            .SelectMany(value => value?.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            .Any(value => string.Equals(
                value,
                HeaderNames.AcceptLanguage,
                StringComparison.OrdinalIgnoreCase));
}
