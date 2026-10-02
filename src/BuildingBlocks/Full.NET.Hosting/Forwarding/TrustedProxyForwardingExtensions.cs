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
    /// <summary>为 IServiceCollection 注册可信代理选项、校验器与 ForwardedHeaders 配置器，但不挂载中间件。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">宿主配置，用于绑定可信代理选项。</param>
    /// <returns>传入的服务集合，便于链式调用。</returns>
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

    /// <summary>在启用时为 IApplicationBuilder 挂载 ForwardedHeaders 中间件；禁用时直接返回以避免空 Known 集合被解释为信任所有来源。</summary>
    /// <param name="application">应用构建器。</param>
    /// <returns>传入的应用构建器，便于链式调用。</returns>
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
