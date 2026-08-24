using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Authorization;
using Full.NET.Modules.Organization.DataScope;
using Full.NET.Modules.Organization.Features.HostUserManagementReference;
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

/// <summary>
/// Organization 业务模块入口：提供租户组织树（部门）、岗位、职级序列、用户隶属关系管理，
/// 并通过 Integration Event 将组织单元变更跨模块发布给 Identity 做投影对账。
/// 事务不变量：机构单元父子层级禁止形成环；用户主部门在同一租户内唯一。
/// </summary>
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
        services.TryAddScoped<HostUserManagementReferenceService>();
        services.TryAddScoped<TenantUserPositionQueryService>();
        services.TryAddScoped<TenantUserPositionManagementService>();
        services.TryAddScoped<TenantPositionQueryService>();
        services.TryAddScoped<TenantPositionManagementService>();
        services.TryAddScoped<TenantPositionLevelQueryService>();
        services.TryAddScoped<TenantPositionLevelManagementService>();
        services.TryAddScoped<TenantUnits.TenantOrganizationUnitDirectory>();
        services.TryAddScoped<TenantUnits.OrganizationUnitProjectionCatalog>();
        services.TryAddScoped<IIdentityOrganizationUnitProjectionSource>(provider =>
            provider.GetRequiredService<TenantUnits.OrganizationUnitProjectionCatalog>());
        services.TryAddScoped<ITenantOrganizationUnitDirectory>(provider =>
            provider.GetRequiredService<TenantUnits.TenantOrganizationUnitDirectory>());
        services.TryAddScoped<IIdentityOrganizationUnitDirectory>(provider =>
            provider.GetRequiredService<TenantUnits.TenantOrganizationUnitDirectory>());
        services.TryAddScoped<IOrganizationOwnedEntityWriteAuthorizer,
            OrganizationOwnedEntityWriteAuthorizer>();
        services.TryAddSingleton<
            IIdentityOrganizationDataScopeSqlProjection,
            IdentityOrganizationDataScopeSqlProjection>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                OrganizationJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.OrganizationDapperAotMaterializerContributor()
            .RegisterMaterializers(new DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>
    /// 注册 Worker 消费 Identity 机构单元投影对账所需的最小 Organization 只读 Port。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddScoped<TenantUnits.OrganizationUnitProjectionCatalog>();
        services.TryAddScoped<IIdentityOrganizationUnitProjectionSource>(provider =>
            provider.GetRequiredService<TenantUnits.OrganizationUnitProjectionCatalog>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageTenantUnits.Endpoint.Map(endpoints);
        Features.ManageTenantUserUnits.Endpoint.Map(endpoints);
        Features.ManageTenantPositions.Endpoint.Map(endpoints);
        Features.ManageTenantPositionLevels.Endpoint.Map(endpoints);
        Features.ManageTenantUserPositions.Endpoint.Map(endpoints);
        Features.HostUserManagementReference.Endpoint.Map(endpoints);
    }
}
