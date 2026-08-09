using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
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
        // 部分装配宿主仍会注册 Dispatcher；提供空目录可保证 DI 图闭合，
        // 完整 Messaging 模块会以同生命周期的真实 Topic/订阅目录替换它。
        services.TryAddScoped(provider =>
            new IntegrationEventSubscriptionCatalog(
                provider.GetServices<IntegrationEventTopicDefinition>(),
                provider.GetServices<IIntegrationEventSubscription>()));
        services.AddScoped<IntegrationEventConsumerDispatcher>();
        return services;
    }
}
