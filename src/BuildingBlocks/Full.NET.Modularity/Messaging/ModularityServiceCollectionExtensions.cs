using Full.NET.Abstractions.Messaging;
using Full.NET.Modularity.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Messaging;

public static class ModularityServiceCollectionExtensions
{
    public static IServiceCollection AddFullNetModularity(this IServiceCollection services)
    {
        if (!services.Any(item => item.ServiceType == typeof(FullNetModuleRegistry)))
        {
            services.AddSingleton(new FullNetModuleRegistry());
        }

        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        return services;
    }
}
