using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Full.NET.Localization;

/// <summary>
/// 按标准 Accept-Language q 值顺序协商语言，并将已登记别名转换为规范标签。
/// </summary>
/// <remarks>
/// 该类型保留基类的候选数量上限语义：先限制 Header 中参与解析的前 N 项，再按 q 值排序，
/// 以避免恶意超长 Header 造成无界 Culture 解析消耗。
/// </remarks>
/// <param name="normalizer">用于识别已登记别名并输出规范标签的语言规范化器。</param>
public sealed class FullNetAcceptLanguageHeaderRequestCultureProvider(
    ILocaleNormalizer normalizer) : AcceptLanguageHeaderRequestCultureProvider
{
    /// <inheritdoc />
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var acceptLanguage = httpContext.Request.GetTypedHeaders().AcceptLanguage;
        if (acceptLanguage is null || acceptLanguage.Count == 0)
        {
            return NullProviderCultureResult;
        }

        var candidates = acceptLanguage.AsEnumerable();
        if (MaximumAcceptLanguageHeaderValuesToTry > 0)
        {
            candidates = candidates.Take(MaximumAcceptLanguageHeaderValuesToTry);
        }

        var canonicalLocales = new List<StringSegment>();
        var seenLocales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.OrderByDescending(
                     value => value,
                     StringWithQualityHeaderValueComparer.QualityComparer))
        {
            var requestedLocale = candidate.Value.ToString();
            // 未知候选必须跳过，不能用默认语言代替，否则会压过后续 q 值较低但受支持的候选。
            if (!normalizer.IsSupported(requestedLocale))
            {
                continue;
            }

            var canonicalLocale = normalizer.Normalize(requestedLocale);
            if (seenLocales.Add(canonicalLocale))
            {
                canonicalLocales.Add(new StringSegment(canonicalLocale));
            }
        }

        return canonicalLocales.Count == 0
            ? NullProviderCultureResult
            : Task.FromResult<ProviderCultureResult?>(
                new ProviderCultureResult(canonicalLocales));
    }
}
