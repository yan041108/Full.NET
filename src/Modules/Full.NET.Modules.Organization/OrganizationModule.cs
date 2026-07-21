using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUnits;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.Organization.Resources;
using Full.NET.Modules.Organization.Serialization;
using Full.NET.Modules.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Organization;

public sealed class OrganizationModule : IFullNetModule
{
    public string Name => "Organization";

    public IReadOnlyCollection<Type> Dependencies =>
    [
        typeof(IdentityModule),
        typeof(TenancyModule),
    ];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddFullNetLocalization();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            OrganizationAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            OrganizationErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<TenantUnitQueryService>();
        services.TryAddScoped<TenantUnitManagementService>();
        services.TryAddScoped<TenantUserUnitQueryService>();
        services.TryAddScoped<TenantUserUnitManagementService>();
        services.TryAddScoped<ITenantOrganizationUnitDirectory, TenantUnits.TenantOrganizationUnitDirectory>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                OrganizationJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageTenantUnits.Endpoint.Map(endpoints);
        Features.ManageTenantUserUnits.Endpoint.Map(endpoints);
    }
}
