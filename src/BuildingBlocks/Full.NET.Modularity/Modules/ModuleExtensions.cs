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
    {
        var registry = GetRegisteredRegistry(services);
        var module = new TModule();
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
