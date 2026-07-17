using System.Globalization;
using Microsoft.Extensions.Options;

namespace Full.NET.Localization;

/// <summary>
/// 基于启动时已验证的语言目录完成规范化与别名映射。
/// </summary>
public sealed class LocaleNormalizer : ILocaleNormalizer
{
    private readonly IReadOnlyDictionary<string, string> _canonicalByInput;

    /// <summary>
    /// 初始化语言规范化器，并固化本次进程使用的语言目录快照。
    /// </summary>
    /// <param name="options">启动阶段验证后的本地化配置。</param>
    public LocaleNormalizer(IOptions<FullNetLocalizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        var supportedLocales = value.SupportedLocales
            .Select(locale => CultureInfo.GetCultureInfo(locale).Name)
            .ToArray();
        SupportedLocales = Array.AsReadOnly(supportedLocales);
        DefaultLocale = CultureInfo.GetCultureInfo(value.DefaultLocale).Name;

        var canonicalByInput = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var locale in supportedLocales)
        {
            canonicalByInput[locale] = locale;
        }

        foreach (var alias in LocaleCatalog.Aliases)
        {
            if (canonicalByInput.TryGetValue(alias.Value, out var canonicalLocale))
            {
                canonicalByInput[alias.Key] = canonicalLocale;
            }
        }

        _canonicalByInput = canonicalByInput;
    }

    /// <inheritdoc />
    public string DefaultLocale { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedLocales { get; }

    /// <inheritdoc />
    public string Normalize(string? requestedLocale) =>
        TryResolve(requestedLocale, out var locale) ? locale : DefaultLocale;

    /// <inheritdoc />
    public bool IsSupported(string? locale) => TryResolve(locale, out _);

    private bool TryResolve(string? requestedLocale, out string locale)
    {
        locale = string.Empty;
        if (string.IsNullOrWhiteSpace(requestedLocale))
        {
            return false;
        }

        var candidate = requestedLocale.Trim();
        if (_canonicalByInput.TryGetValue(candidate, out var directLocale))
        {
            locale = directLocale;
            return true;
        }

        try
        {
            var cultureName = CultureInfo.GetCultureInfo(candidate).Name;
            if (_canonicalByInput.TryGetValue(cultureName, out var normalizedLocale))
            {
                locale = normalizedLocale;
                return true;
            }

            return false;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
