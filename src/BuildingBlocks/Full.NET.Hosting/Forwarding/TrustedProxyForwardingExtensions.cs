using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Forwarding;

/// <summary>
/// 注册并启用 Full.NET 的可信代理转发边界。
/// </summary>
public static class TrustedProxyForwardingExtensions
{
    public static IServiceCollection AddFullNetTrustedProxyForwarding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(configuration);

        services.AddOptions<TrustedProxyOptions>()
            .BindConfiguration(TrustedProxyOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TrustedProxyOptions>,
            TrustedProxyOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<ForwardedHeadersOptions>,
            TrustedProxyForwardedHeadersConfigurator>());
        return services;
    }

    public static IApplicationBuilder UseFullNetTrustedProxyForwarding(
        this IApplicationBuilder application)
    {
        var options = application.ApplicationServices
            .GetRequiredService<IOptions<TrustedProxyOptions>>()
            .Value;

        // 禁用时不挂载框架中间件，避免空 Known 集合被未来框架行为解释为信任所有来源。
        return options.Enabled
            ? application.UseForwardedHeaders()
            : application;
    }
}
