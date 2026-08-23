using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Notifications.Resources;
using Full.NET.Modules.Notifications.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Notifications;

/// <summary>
/// 通知中心模块：负责 Host 公告、站内信收件箱与基于 SignalR 的实时推送。
/// </summary>
/// <remarks>
/// 模块依赖 Identity 以解析 Host 用户目录；所有站内信与公告均属 Host 作用域，
/// 不携带租户边界。业务状态变更与实时修复事件通过同事务 Outbox 原子提交，
/// 实时推送仅作为低延迟广播，最终一致性由 Outbox 消费者保证。
/// </remarks>
public sealed class NotificationsModule : IFullNetModule
{
    public string Name => "Notifications";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddBackgroundServices(services, configuration);
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
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                NotificationsJsonSerializerContext.Default));
    }

    /// <summary>
    /// 注册 Worker 修复 Notifications 实时投递所需的最小后台能力。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
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

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostAnnouncements.Endpoint.Map(endpoints);
        Features.ManageMyInboxMessages.Endpoint.Map(endpoints);
        Features.SendHostInboxMessages.Endpoint.Map(endpoints);
    }
}
