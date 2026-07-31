using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.DataScope;
using Full.NET.Modules.Organization.Features.ListAssignableHostUsers;
using Full.NET.Modules.Organization.Features.ManageTenantPositionLevels;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUnits;
using Full.NET.Modules.Organization.Features.ManageTenantUserPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.Organization.Resources;
using Full.NET.Modules.Organization.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Organization;

public sealed class OrganizationModule : IFullNetModule
{
    public string Name => "Organization";

    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
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
        services.TryAddScoped<AssignableHostUserQueryService>();
        services.TryAddScoped<TenantUserPositionQueryService>();
        services.TryAddScoped<TenantUserPositionManagementService>();
        services.TryAddScoped<TenantPositionQueryService>();
        services.TryAddScoped<TenantPositionManagementService>();
        services.TryAddScoped<TenantPositionLevelQueryService>();
        services.TryAddScoped<TenantPositionLevelManagementService>();
        services.TryAddScoped<TenantUnits.TenantOrganizationUnitDirectory>();
        services.TryAddScoped<ITenantOrganizationUnitDirectory>(provider =>
            provider.GetRequiredService<TenantUnits.TenantOrganizationUnitDirectory>());
        services.TryAddScoped<IIdentityOrganizationUnitDirectory>(provider =>
            provider.GetRequiredService<TenantUnits.TenantOrganizationUnitDirectory>());
        services.TryAddSingleton<
            IIdentityOrganizationDataScopeSqlProjection,
            IdentityOrganizationDataScopeSqlProjection>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                OrganizationJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageTenantUnits.Endpoint.Map(endpoints);
        Features.ManageTenantUserUnits.Endpoint.Map(endpoints);
        Features.ManageTenantPositions.Endpoint.Map(endpoints);
        Features.ManageTenantPositionLevels.Endpoint.Map(endpoints);
        Features.ManageTenantUserPositions.Endpoint.Map(endpoints);
    }
}
