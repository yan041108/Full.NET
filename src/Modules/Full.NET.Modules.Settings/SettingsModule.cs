using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageHostDictTypes;
using Full.NET.Modules.Settings.Resources;
using Full.NET.Modules.Settings.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Settings;

public sealed class SettingsModule : IFullNetModule
{
    public string Name => "Settings";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            SettingsAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IEnumCatalogContributor,
            Catalogs.SettingsEnumCatalogContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            SettingsErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<Catalogs.EnumCatalogRegistry>();
        services.TryAddScoped<HostDictTypeQueryService>();
        services.TryAddScoped<HostDictTypeManagementService>();
        services.TryAddScoped<Features.ManageHostDictItems.HostDictItemQueryService>();
        services.TryAddScoped<Features.ManageHostDictItems.HostDictItemManagementService>();
        services.TryAddScoped<Features.ManageTenantDictTypes.TenantDictTypeQueryService>();
        services.TryAddScoped<Features.ManageTenantDictTypes.TenantDictTypeManagementService>();
        services.TryAddScoped<Features.ManageTenantDictItems.TenantDictItemQueryService>();
        services.TryAddScoped<Features.ManageTenantDictItems.TenantDictItemManagementService>();
        services.TryAddScoped<Features.ManageHostConfigEntries.HostConfigEntryQueryService>();
        services.TryAddScoped<Features.ManageHostConfigEntries.HostConfigEntryManagementService>();
        services.TryAddScoped<Features.QueryHostEnumCatalogs.HostEnumCatalogQueryService>();
        services.TryAddScoped<
            Features.ManageMyGridPreferences.MyGridPreferenceService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                SettingsJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostDictTypes.Endpoint.Map(endpoints);
        Features.ManageHostDictItems.Endpoint.Map(endpoints);
        Features.ManageTenantDictTypes.Endpoint.Map(endpoints);
        Features.ManageTenantDictItems.Endpoint.Map(endpoints);
        Features.ManageHostConfigEntries.Endpoint.Map(endpoints);
        Features.QueryHostEnumCatalogs.Endpoint.Map(endpoints);
        Features.ManageMyGridPreferences.Endpoint.Map(endpoints);
    }
}
