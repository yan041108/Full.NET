using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Modules;

public static class ModuleExtensions
{
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
