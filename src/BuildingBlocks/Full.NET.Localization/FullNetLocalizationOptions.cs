using System.Globalization;
using Microsoft.Extensions.Options;

namespace Full.NET.Localization;

/// <summary>
/// 定义 Full.NET 服务端支持的规范语言与默认回退语言。
/// </summary>
/// <remarks>
/// 运行时只消费已编译并在启动阶段验证的配置；请求处理过程不会读取治理 JSON 文件。
/// </remarks>
public sealed class FullNetLocalizationOptions
{
    /// <summary>
    /// 获取或设置无法协商语言时使用的规范语言标签。
    /// </summary>
    public string DefaultLocale { get; set; } = LocaleCatalog.DefaultLocale;

    /// <summary>
    /// 获取或设置可用于服务端资源查找的规范语言标签列表。
    /// </summary>
    public List<string> SupportedLocales { get; set; } = [.. LocaleCatalog.SupportedLocales];
}

/// <summary>
/// 验证本地化配置可安全用于 <see cref="CultureInfo"/> 与请求协商。
/// </summary>
public sealed class FullNetLocalizationOptionsValidator :
    IValidateOptions<FullNetLocalizationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(
        string? name,
        FullNetLocalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.SupportedLocales is null || options.SupportedLocales.Count == 0)
        {
            failures.Add("SupportedLocales must contain at least one locale.");
            return ValidateOptionsResult.Fail(failures);
        }

        var normalizedLocales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var locale in options.SupportedLocales)
        {
            if (!TryGetCultureName(locale, out var cultureName))
            {
                failures.Add($"Supported locale '{locale}' is not a valid CultureInfo name.");
                continue;
            }

            if (!normalizedLocales.Add(cultureName))
            {
                failures.Add($"Supported locale '{locale}' is duplicated.");
            }
        }

        if (!TryGetCultureName(options.DefaultLocale, out var defaultCultureName))
        {
            failures.Add("DefaultLocale is not a valid CultureInfo name.");
        }
        else if (!normalizedLocales.Contains(defaultCultureName))
        {
            failures.Add("DefaultLocale must be present in SupportedLocales.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool TryGetCultureName(string? locale, out string cultureName)
    {
        cultureName = string.Empty;
        if (string.IsNullOrWhiteSpace(locale))
        {
            return false;
        }

        try
        {
            cultureName = CultureInfo.GetCultureInfo(locale.Trim()).Name;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
