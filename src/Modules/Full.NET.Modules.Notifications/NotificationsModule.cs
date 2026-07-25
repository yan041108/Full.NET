using Full.NET.Abstractions.Ids;
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

public sealed class NotificationsModule : IFullNetModule
{
    public string Name => "Notifications";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
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

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostAnnouncements.Endpoint.Map(endpoints);
        Features.ManageMyInboxMessages.Endpoint.Map(endpoints);
        Features.SendHostInboxMessages.Endpoint.Map(endpoints);
    }
}
