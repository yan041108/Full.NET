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
/// 模块依赖 Identity 解析用户目录，并依赖 Organization 校验机构受众；Host 与 Tenant 站内信共用一张表，作用域只来自受信会话。
/// Workflow 只作为可选事件生产者；未启用 Workflow 时通知中心仍可独立提供公告、站内信和渠道投递。
/// Delivery HostedService 只在 Worker 的 <see cref="AddBackgroundServices"/> 注册，避免 API 进程启动领取循环。
/// </remarks>
public sealed class NotificationsModule : IFullNetModule
{
    /// <summary>匿名回执入口限流策略名；按连接 IP 分区。</summary>
    internal const string ProviderReceiptRateLimitPolicy = "notifications-provider-receipts";

    /// <summary>收件端点验证码发送限流策略名。</summary>
    internal const string RecipientEndpointVerificationSendRateLimitPolicy =
        "notifications-recipient-endpoint-verification-send";

    /// <summary>收件端点验证码校验限流策略名。</summary>
    internal const string RecipientEndpointVerificationVerifyRateLimitPolicy =
        "notifications-recipient-endpoint-verification-verify";

    /// <summary>获取通知中心模块名称。</summary>
    public string Name => "Notifications";

    /// <summary>获取通知中心运行所需的模块依赖。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity", "Organization"];

    /// <summary>获取仅用于异步提醒投影的可选事件生产者模块。</summary>
    public IReadOnlyCollection<string> OptionalContractDependencies => ["Workflow"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterRealtimeHandlers(services);
        RegisterWorkflowNotificationProjection(services);
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
        services.TryAddScoped<Features.ManageHostAnnouncements.HostAnnouncementAudienceValidator>();
        services.TryAddScoped<Features.ManageHostAnnouncements.HostAnnouncementManagementService>();
        services.TryAddScoped<Features.ManageMyInboxMessages.MyInboxQueryService>();
        services.TryAddScoped<Features.ManageMyInboxMessages.MyInboxManagementService>();
        services.TryAddScoped<Features.SendHostInboxMessages.HostInboxMessageService>();
        services.TryAddScoped<Features.SendTenantInboxMessages.TenantInboxMessageService>();
        services.TryAddScoped<Features.ProjectInboxFromIntent.InboxIntentProjectionService>();
        services.TryAddScoped<Features.ManageTemplates.NotificationTemplateService>();
        services.TryAddScoped<Features.ManageTemplates.NotificationTemplateSelector>();
        services.TryAddScoped<Features.CreateNotificationIntents.NotificationIntentService>();
        services.TryAddScoped<Features.ManageProviderProfiles.NotificationProviderProfileService>();
        services.TryAddScoped<Features.ManageBindings.NotificationBindingService>();
        services.TryAddScoped<Features.ManageDeliveries.NotificationDeliveryService>();
        services.TryAddScoped<Features.ReceiveProviderReceipts.NotificationReceiptProcessor>();
        services.TryAddSingleton<Providers.INotificationProviderTypeCatalog, Providers.NotificationProviderTypeCatalog>();
        services.TryAddScoped<Features.ManageRecipientEndpoints.RecipientEndpointStore>();
        services.TryAddScoped<Features.VerifyRecipientEndpoints.RecipientEndpointVerificationService>();
        services.TryAddScoped<Features.VerifyRecipientEndpoints.IRecipientEndpointVerificationMailSender,
            Features.VerifyRecipientEndpoints.SmtpRecipientEndpointVerificationMailSender>();
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
        RegisterWorkflowNotificationProjection(services);
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
        Features.VerifyRecipientEndpoints.Endpoint.Map(endpoints);
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

    /// <summary>注册 Workflow 可靠提醒事件的 Intent/Inbox 投影闭包。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    private static void RegisterWorkflowNotificationProjection(IServiceCollection services)
    {
        services.TryAddSingleton<Providers.INotificationProviderTypeCatalog, Providers.NotificationProviderTypeCatalog>();
        services.TryAddScoped<Features.ProjectInboxFromIntent.InboxIntentProjectionService>();
        services.TryAddScoped<Features.CreateNotificationIntents.NotificationRecipientDirectoryResolver>();
        services.TryAddScoped<Features.ManageTemplates.NotificationTemplateSelector>();
        services.TryAddScoped<Features.CreateNotificationIntents.NotificationIntentService>();
        services.TryAddScoped<Features.ProjectWorkflowNotifications.WorkflowNotificationTemplateProvisioner>();
        services.TryAddScoped<Features.ProjectWorkflowNotifications.WorkflowNotificationProjectionService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            Features.ProjectWorkflowNotifications.WorkflowTodoAssignedIntegrationEventHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            Features.ProjectWorkflowNotifications.WorkflowTodoReminderRequestedIntegrationEventHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            Features.ProjectWorkflowNotifications.WorkflowTodoEscalationRequestedIntegrationEventHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            Features.ProjectWorkflowNotifications.WorkflowInstanceCompletedIntegrationEventHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            Features.ProjectWorkflowNotifications.WorkflowInstanceRejectedIntegrationEventHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            Features.ProjectWorkflowNotifications.WorkflowInstanceCancelledIntegrationEventHandler>());
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
