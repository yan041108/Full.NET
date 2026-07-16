using Full.NET.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Messaging;

public static class ModularityServiceCollectionExtensions
{
    public static IServiceCollection AddFullNetModularity(this IServiceCollection services)
    {
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        return services;
    }
}
