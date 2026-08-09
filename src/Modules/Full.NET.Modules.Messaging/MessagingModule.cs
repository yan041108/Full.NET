using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.GetDeadLetters;
using Full.NET.Modules.Messaging.Features.GetDeliveryStatus;
using Full.NET.Modules.Messaging.Features.ReplayDeadLetter;
using Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Messaging.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Messaging;

public sealed class MessagingModule : IFullNetModule
{
    public string Name => "Messaging";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterMessagingCore(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            MessagingAuthorizationContributor>());
        services.TryAddScoped<DeadLetterQueryService>();
        services.TryAddScoped<DeadLetterReplayService>();
        services.TryAddScoped<DeliveryStatusQueryService>();
        services.TryAddScoped<DeliveryCutoverService>();
        services.TryAddScoped<DeliveryRollbackService>();
        services.TryAddScoped<
            ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>,
            MessagingDomainAuditWriter>();
        RegisterSubscriptionCatalog(services);
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                MessagingJsonSerializerContext.Default));
    }

    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterMessagingCore(services);
        RegisterSubscriptionCatalog(services);
    }

    private static void RegisterMessagingCore(IServiceCollection services)
    {
        // 修复意图：Topic 目录条目是带 TopicCode/EventType/SchemaVersion 元数据的单例值对象，
        // 不是按实现类型区分的策略接口。TryAddEnumerable 对工厂返回相同 ServiceType 的描述符
        // 会抛 ArgumentException（无法区分两个 Func<,> 注册是否应该都保留），因此改为：
        // 1) 按稳定语义键 TopicCode 先去重（而非 ReferenceEquals，避免不同装配阶段重新 new 同语义条目）
        // 2) 直接用 AddSingleton(实例) 注入，保证 API/Worker 两次 AddServices/AddBackgroundServices
        //    都幂等不抛错。
        var topic = MessagingTopicDefinitions.OrganizationUnitChanged;
        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(IntegrationEventTopicDefinition)
                && descriptor.ImplementationInstance is IntegrationEventTopicDefinition existing
                && existing.TopicCode == topic.TopicCode))
        {
            services.AddSingleton(topic);
        }

        services.TryAddScoped<EventStreamOwnershipStore>();
        services.TryAddScoped<IEventStreamOwnershipStore>(
            provider => provider.GetRequiredService<EventStreamOwnershipStore>());
        services.TryAddScoped<IEffectiveEventDeliveryOwnerResolver, EffectiveEventDeliveryOwnerResolver>();
    }

    private static void RegisterSubscriptionCatalog(IServiceCollection services)
    {
        // 先移除 Modularity 核心注册的空目录默认值（RemoveAll 按服务类型匹配，同时移除接口和具体类）。
        services.RemoveAll<IIntegrationEventSubscriptionCatalog>();
        services.RemoveAll<IntegrationEventSubscriptionCatalog>();
        // Catalog 生命周期说明：
        // 必须是 Scoped：
        // 1) KafkaConsumerWorker 是 Singleton HostedService，不得直接持有 scoped 订阅 Handler；
        // 2) 每次消费消息通过 IServiceScopeFactory.CreateAsyncScope 创建独立作用域，
        //    与 Inbox 事务作用域、Handler 解析作用域保持一致，避免跨请求共享状态。
        services.AddScoped<IIntegrationEventSubscriptionCatalog>(provider =>
            new IntegrationEventSubscriptionCatalog(
                provider.GetServices<IntegrationEventTopicDefinition>(),
                provider.GetServices<IIntegrationEventSubscription>()));
        // 同时注册具体类，兼容直接解析 IntegrationEventSubscriptionCatalog 的既有代码。
        services.AddScoped(provider =>
            (IntegrationEventSubscriptionCatalog)provider
                .GetRequiredService<IIntegrationEventSubscriptionCatalog>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.GetDeadLetters.Endpoint.Map(endpoints);
        Features.ReplayDeadLetter.Endpoint.Map(endpoints);
        Features.GetDeliveryStatus.Endpoint.Map(endpoints);
        Features.ChangeDeliveryOwner.Endpoint.Map(endpoints);
        Features.RollbackDeliveryOwner.Endpoint.Map(endpoints);
    }
}
