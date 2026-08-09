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

        // 修复意图：精简宿主（未加载 Messaging 模块）的 DI 图闭合。
        // AddFullNetModularity 会注册 IntegrationEventConsumerDispatcher，Dispatcher 构造依赖
        // IIntegrationEventSubscriptionCatalog；如果不提供默认空实现，Messaging 模块未加载时
        // services.BuildServiceProvider() / ValidateOnBuild 会抛 InvalidOperationException。
        // 使用 TryAddScoped：MessagingModule 随后会 RemoveAll 并替换为真实目录，不会重复注册。
        services.TryAddScoped<IIntegrationEventSubscriptionCatalog, EmptyIntegrationEventSubscriptionCatalog>();
        services.TryAddScoped<IEffectiveEventDeliveryOwnerResolver, LegacyPollingEventDeliveryOwnerResolver>();
        // 同时注册具体类的空包装，兼容直接解析 IntegrationEventSubscriptionCatalog 的既有代码；
        // 空宿主中即使解析具体类也返回空集合，避免 DI 验证失败。
        services.TryAddScoped(provider =>
            new IntegrationEventSubscriptionCatalog(
                provider.GetServices<IntegrationEventTopicDefinition>(),
                provider.GetServices<IIntegrationEventSubscription>()));
        services.AddScoped<IntegrationEventConsumerDispatcher>();
        return services;
    }
}
