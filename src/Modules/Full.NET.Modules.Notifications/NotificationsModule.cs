using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.RateLimiting;
using Full.NET.Modules.Notifications.Resources;
using Full.NET.Modules.Notifications.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

namespace Full.NET.Modules.Notifications;

/// <summary>
/// 通知中心模块：负责 Host 公告、站内信收件箱、渠道投递 Worker 与基于 SignalR 的实时推送。
/// </summary>
/// <remarks>
/// 模块依赖 Identity 以解析用户目录；Host 与 Tenant 站内信共用一张表，作用域只来自受信会话。
/// Delivery HostedService 只在 Worker 的 <see cref="AddBackgroundServices"/> 注册，避免 API 进程启动领取循环。
/// </remarks>
public sealed class NotificationsModule : IFullNetModule
{
    /// <summary>匿名回执入口限流策略名；按连接 IP 分区。</summary>
    internal const string ProviderReceiptRateLimitPolicy = "notifications-provider-receipts";

    public string Name => "Notifications";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterRealtimeHandlers(services);
        RegisterDeliveryCore(services, configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<RateLimiterOptions>,
            NotificationsRateLimiterPolicyConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<RateLimitPolicyErrorCodes>,
            NotificationsRateLimiterPolicyConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            NotificationsAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            NotificationsErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<Features.ManageHostAnnouncements.HostAnnouncementQueryService>();
        services.TryAddScoped<Features.ManageHostAnnouncements.HostAnnouncementManagementService>();
        services.TryAddScoped<Features.ManageMyInboxMessages.MyInboxQueryService>();
        services.TryAddScoped<Features.ManageMyInboxMessages.MyInboxManagementService>();
        services.TryAddScoped<Features.SendHostInboxMessages.HostInboxMessageService>();
        services.TryAddScoped<Features.SendTenantInboxMessages.TenantInboxMessageService>();
        services.TryAddScoped<Features.ProjectInboxFromIntent.InboxIntentProjectionService>();
        services.TryAddScoped<Features.ManageTemplates.NotificationTemplateService>();
        services.TryAddScoped<Features.CreateNotificationIntents.NotificationIntentService>();
        services.TryAddScoped<Features.ManageProviderProfiles.NotificationProviderProfileService>();
        services.TryAddScoped<Features.ManageBindings.NotificationBindingService>();
        services.TryAddScoped<Features.ManageDeliveries.NotificationDeliveryService>();
        services.TryAddScoped<Features.ReceiveProviderReceipts.NotificationReceiptProcessor>();
        services.TryAddSingleton<Providers.INotificationProviderTypeCatalog, Providers.NotificationProviderTypeCatalog>();
        services.TryAddScoped<Features.ManageRecipientEndpoints.RecipientEndpointStore>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                NotificationsJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.NotificationsDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>
    /// 注册 Worker 修复 Notifications 实时投递与 Delivery 领取循环所需的最小后台能力。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.NotificationsDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        RegisterRealtimeHandlers(services);
        RegisterDeliveryCore(services, configuration);
        services.AddOptions<NotificationDeliveryWorkerOptions>()
            .Bind(configuration.GetSection(NotificationDeliveryWorkerOptions.SectionName))
            .ValidateOnStart();
        services.AddHostedService<NotificationDeliveryHostedProcessor>();
        services
            .AddOpenTelemetry()
            .WithMetrics(metrics =>
                metrics.AddMeter(NotificationDeliveryTelemetry.MeterName));
    }

    /// <summary>映射 Notifications 模块全部受保护和公开 HTTP 路由。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostAnnouncements.Endpoint.Map(endpoints);
        Features.ManageMyInboxMessages.Endpoint.Map(endpoints);
        Features.SendHostInboxMessages.Endpoint.Map(endpoints);
        Features.SendTenantInboxMessages.Endpoint.Map(endpoints);
        Features.ManageTemplates.Endpoint.Map(endpoints);
        Features.CreateNotificationIntents.Endpoint.Map(endpoints);
        Features.ManageProviderProfiles.Endpoint.Map(endpoints);
        Features.ManageBindings.Endpoint.Map(endpoints);
        Features.ManageDeliveries.Endpoint.Map(endpoints);
        Features.ReceiveProviderReceipts.Endpoint.Map(endpoints);
        Features.ManageRecipientEndpoints.Endpoint.Map(endpoints);
    }

    private static void RegisterRealtimeHandlers(IServiceCollection services)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.NotificationsDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddScoped<NotificationRealtimeDelivery>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            AnnouncementPublishedRealtimeHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            InboxMessageReceivedRealtimeHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            InboxReadStateChangedRealtimeHandler>());
    }

    /// <summary>
    /// API 与 Worker 共用 BatchProcessor/Options 默认值；HostedService 与配置绑定只在 Worker 入口追加。
    /// </summary>
    private static void RegisterDeliveryCore(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<NotificationDeliveryWorkerOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<NotificationDeliveryWorkerOptions>,
            NotificationDeliveryWorkerOptionsValidator>());
        services.TryAddScoped<NotificationDeliveryBatchProcessor>();
        services.TryAddScoped<Features.ReceiveProviderReceipts.NotificationReceiptProcessor>();
        services.TryAddScoped<Features.ManageDeliveries.NotificationDeliveryService>();
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<Domain.NotificationRecipientEndpointProtector>();
        if (configuration.GetValue<bool>("Notifications:Providers:Smtp:Enabled"))
        {
            services.TryAddSingleton<Providers.Smtp.INotificationSecretResolver,
                Providers.Smtp.EnvironmentNotificationSecretResolver>();
            services.TryAddSingleton<Providers.Smtp.ISmtpMailTransport,
                Providers.Smtp.MailKitSmtpTransport>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<
                Providers.INotificationProviderAdapter,
                Providers.Smtp.SmtpNotificationProviderAdapter>());
        }
    }
}
