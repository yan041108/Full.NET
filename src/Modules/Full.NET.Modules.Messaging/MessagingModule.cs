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
using Full.NET.Modules.Messaging.Features.ReplayKafkaRange;
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

/// <summary>
/// 消息运维模块：提供事件交付所有权切换/回退、消费死信与 Kafka 范围重放、以及 Outbox 积压监控能力。
/// </summary>
/// <remarks>
/// 模块依赖 Identity；所有运维操作归属 Host 作用域并绑定独立稳定权限码。
/// 交付所有权切换通过 CAS 守卫避免并发双发布，切换前必须排空 Legacy 积压并完成影子验证；
/// 死信与范围重放保持消费幂等，重放只触发既定业务的幂等副作用。
/// 订阅目录按请求作用域解析，避免 Singleton Worker 跨消息持有 scoped Handler。
/// </remarks>
public sealed class MessagingModule : IFullNetModule
{
    public string Name => "Messaging";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterMessagingCore(services);
        MessagingNativeKafkaReplayTestHarness.RegisterIfTesting(services, configuration);
        services.AddOptions<DeliveryCutoverOptions>()
            .Bind(configuration.GetSection(DeliveryCutoverOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            MessagingAuthorizationContributor>());
        services.TryAddScoped<DeadLetterQueryService>();
        services.TryAddScoped<DeadLetterReplayService>();
        services.TryAddScoped<KafkaRangeReplayOperationsService>();
        services.TryAddScoped<DeliveryStatusQueryService>();
        services.TryAddScoped<DeliveryCutoverService>();
        services.TryAddScoped<DeliveryRollbackService>();
        services.TryAddSingleton<
            IEventDeliveryRollbackReadinessReader,
            FailClosedEventDeliveryRollbackReadinessReader>();
        services.TryAddScoped<
            ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>,
            MessagingDomainAuditWriter>();
        RegisterSubscriptionCatalog(services);
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                MessagingJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.MessagingDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterMessagingCore(services);
        MessagingNativeKafkaReplayTestHarness.RegisterIfTesting(services, configuration);
        RegisterSubscriptionCatalog(services);
    }

    private static void RegisterMessagingCore(IServiceCollection services)
    {
        services.TryAddScoped<EventStreamOwnershipStore>();
        services.TryAddScoped<IEventStreamOwnershipStore>(
            provider => provider.GetRequiredService<EventStreamOwnershipStore>());
        services.RemoveAll<IEffectiveEventDeliveryOwnerResolver>();
        services.AddScoped<IEffectiveEventDeliveryOwnerResolver, EffectiveEventDeliveryOwnerResolver>();
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
        Features.ReplayKafkaRange.Endpoint.Map(endpoints);
        Features.GetDeliveryStatus.Endpoint.Map(endpoints);
        Features.ChangeDeliveryOwner.Endpoint.Map(endpoints);
        Features.RollbackDeliveryOwner.Endpoint.Map(endpoints);
    }
}
