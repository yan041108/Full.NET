using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modularity.Modules;

/// <summary>
/// 模块装配辅助扩展；提供从模块注册、端点映射、中间件注入到目录快照物化的完整链式 API，
/// 供宿主 Composition 层在启动时以声明式方式编排模块。
/// </summary>
public static class ModuleExtensions
{
    /// <summary>
    /// 通过无参构造器实例化并注册模块；适用于不含启动时参数的标准模块。
    /// </summary>
    public static IServiceCollection AddFullNetModule<TModule>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TModule : class, IFullNetModule, new()
        => services.AddFullNetModule(new TModule(), configuration);

    /// <summary>
    /// 注册一个已实例化的模块，供集中目录以统一顺序装配，避免宿主复制模块清单。
    /// </summary>
    public static IServiceCollection AddFullNetModule(
        this IServiceCollection services,
        IFullNetModule module,
        IConfiguration configuration)
    {
        var registry = GetRegisteredRegistry(services);
        registry.Add(module);
        module.AddServices(services, configuration);
        return services;
    }

    /// <summary>
    /// 按模块依赖顺序遍历所有已注册模块，依次调用其 <see cref="IFullNetModule.MapEndpoints"/>，
    /// 确保先置模块（如多租户、认证）的路由约定在业务模块之前生效。
    /// </summary>
    public static IEndpointRouteBuilder MapFullNetModules(
        this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider
            .GetRequiredService<FullNetModuleRegistry>();
        foreach (var module in registry.GetOrderedModules())
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }

    /// <summary>
    /// 在指定管道阶段按模块依赖顺序统一应用各模块中间件，使宿主无需直接引用具体模块。
    /// </summary>
    public static IApplicationBuilder UseFullNetModuleMiddleware(
        this IApplicationBuilder app,
        ModulePipelineStage stage)
    {
        var registry = app.ApplicationServices
            .GetRequiredService<FullNetModuleRegistry>();
        foreach (var module in registry.GetOrderedModules())
        {
            module.UseModuleMiddleware(app, stage);
        }

        return app;
    }

    /// <summary>
    /// 读取服务集合中已注册的模块注册表实例，供 Composition 在装配期物化只读清单。
    /// </summary>
    public static FullNetModuleRegistry GetFullNetModuleRegistry(
        this IServiceCollection services) =>
        GetRegisteredRegistry(services);

    /// <summary>
    /// 基于当前注册表物化不可变模块清单，并注册为单例供 Host 只读查询。
    /// </summary>
    public static IServiceCollection AddFullNetModuleCatalogSnapshot(
        this IServiceCollection services,
        Func<IFullNetModule, FullNetModuleDescriptor> descriptorFactory)
    {
        ArgumentNullException.ThrowIfNull(descriptorFactory);
        var registry = GetRegisteredRegistry(services);
        var snapshot = FullNetModuleCatalogSnapshot.FromRegistry(
            registry,
            descriptorFactory);
        // Api Profile 必须替换 AddFullNetModularity 注册的空清单。
        services.RemoveAll<IFullNetModuleCatalog>();
        services.AddSingleton<IFullNetModuleCatalog>(snapshot);
        return services;
    }

    internal static FullNetModuleRegistry GetRegisteredRegistry(
        IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(
            item => item.ServiceType == typeof(FullNetModuleRegistry));
        return descriptor?.ImplementationInstance as FullNetModuleRegistry
            ?? throw new InvalidOperationException(
                "Call AddFullNetModularity before registering modules.");
    }
}
