using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modularity.Messaging;

/// <summary>
/// Modularity 核心服务注册扩展；集中注册模块注册表、CQRS 分发器、集成事件调度器
/// 及其默认空回退实现，确保精简宿主（未加载 Messaging 模块）的 DI 图也能闭合。
/// </summary>
public static class ModularityServiceCollectionExtensions
{
    /// <summary>
    /// 向服务集合注册 Modularity 基础设施：
    /// 单例 <c>FullNetModuleRegistry</c>、默认空目录 <c>IFullNetModuleCatalog</c>、
    /// Scoped CQRS 分发器、集成事件消费者调度器，以及空订阅目录安全回退。
    /// </summary>
    /// <remarks>
    /// 幂等调用安全：对 <c>FullNetModuleRegistry</c> 与默认空实现均采用 TryAdd 语义，
    /// 多次调用不会重复注册或覆盖已存在的真实实现（如 Messaging 模块替换的订阅目录）。
    /// </remarks>
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
