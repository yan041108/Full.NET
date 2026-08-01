using Full.NET.Abstractions.Messaging;
using Full.NET.Modularity.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modularity.Messaging;

public static class ModularityServiceCollectionExtensions
{
    public static IServiceCollection AddFullNetModularity(this IServiceCollection services)
    {
        if (!services.Any(item => item.ServiceType == typeof(FullNetModuleRegistry)))
        {
            services.AddSingleton(new FullNetModuleRegistry());
        }

        // 默认空清单保证部分装配（如 Integration 事务夹具）可解析查询服务；Api Profile 会替换为真实快照。
        services.TryAddSingleton<IFullNetModuleCatalog>(FullNetModuleCatalogSnapshot.Empty);

        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        return services;
    }
}
