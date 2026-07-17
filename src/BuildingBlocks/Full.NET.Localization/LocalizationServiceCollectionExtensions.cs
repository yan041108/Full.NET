using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Localization;

/// <summary>
/// 注册 Full.NET 服务端语言协商与执行上下文服务。
/// </summary>
public static class LocalizationServiceCollectionExtensions
{
    /// <summary>
    /// 注册本地化资源、配置验证、语言规范化器和仅基于 Accept-Language 的请求协商。
    /// </summary>
    /// <param name="services">应用服务集合。</param>
    /// <returns>原服务集合，便于链式注册。</returns>
    public static IServiceCollection AddFullNetLocalization(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLocalization();
        services
            .AddOptions<FullNetLocalizationOptions>()
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<FullNetLocalizationOptions>,
                FullNetLocalizationOptionsValidator>());
        services.TryAddSingleton<ILocaleNormalizer, LocaleNormalizer>();
        services.TryAddSingleton<ILocaleContext, LocaleContext>();

        services
            .AddOptions<RequestLocalizationOptions>()
            .Configure<IOptions<FullNetLocalizationOptions>, ILocaleNormalizer>(
                static (requestOptions, localizationOptions, normalizer) =>
                {
                    var options = localizationOptions.Value;
                    var supportedCultures = options.SupportedLocales
                        .Select(CultureInfo.GetCultureInfo)
                        .ToArray();

                    requestOptions.DefaultRequestCulture =
                        new RequestCulture(options.DefaultLocale);
                    requestOptions.SupportedCultures = supportedCultures;
                    requestOptions.SupportedUICultures = supportedCultures;
                    requestOptions.RequestCultureProviders.Clear();
                    requestOptions.RequestCultureProviders.Add(
                        new FullNetAcceptLanguageHeaderRequestCultureProvider(normalizer));
                });

        return services;
    }
}
