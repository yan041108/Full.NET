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
using Full.NET.Modules.Organization.Features.ImportExport;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.ImportExport.Contracts;
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
    /// <summary>
    /// Organization 模块的唯一稳定标识，固定为 "Organization"。
    /// </summary>
    public string Name => "Organization";

    /// <summary>
    /// Organization 模块依赖 Identity 与 Tenancy 模块，需在其后加载。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
    ];

    /// <summary>岗位静态导入扩展 ImportExport 合同；最小预设不含导入模块时组织主路径仍可运行。</summary>
    public IReadOnlyCollection<string> OptionalContractDependencies => ["ImportExport"];

    /// <summary>
    /// 注册 Organization 模块的应用服务：租户部门树、岗位、职级、用户隶属关系管理，
    /// 组织单元投影目录、工作流单位负责人目录、数据范围 SQL 投影、岗位静态导入，
    /// 以及授权目录、错误资源、本地化与 AOT 物化器。
    /// </summary>
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
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            ITenantOrganizationUserMembershipReader,
            HostDashboard.TenantOrganizationUserMembershipReader>());
        services.TryAddScoped<IWorkflowUnitLeaderDirectory, TenantUnits.WorkflowUnitLeaderDirectory>();
        services.TryAddScoped<IIdentityOrganizationUnitDirectory>(provider =>
            provider.GetRequiredService<TenantUnits.TenantOrganizationUnitDirectory>());
        services.TryAddScoped<TenantUnits.OrganizationPositionDirectory>();
        services.TryAddScoped<IIdentityOrganizationPositionDirectory>(provider =>
            provider.GetRequiredService<TenantUnits.OrganizationPositionDirectory>());
        services.TryAddScoped<IOrganizationOwnedEntityWriteAuthorizer,
            OrganizationOwnedEntityWriteAuthorizer>();
        services.TryAddSingleton<
            IIdentityOrganizationDataScopeSqlProjection,
            IdentityOrganizationDataScopeSqlProjection>();
        services.TryAddScoped<TenantPositionImportPreviewService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IStaticImportSchemaHandler,
            TenantPositionsStaticImportSchemaHandler>());
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
#if FULLNET_AOT_COMPILE
        new Persistence.OrganizationDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        // 后台导入复用当前机构及有效隶属校验，不能因 Worker 没有 HTTP 入口而省略写授权。
        services.TryAddScoped<TenantUnits.TenantOrganizationUnitDirectory>();
        services.TryAddScoped<ITenantOrganizationUnitDirectory>(provider => provider.GetRequiredService<TenantUnits.TenantOrganizationUnitDirectory>());
        services.TryAddScoped<IOrganizationOwnedEntityWriteAuthorizer, OrganizationOwnedEntityWriteAuthorizer>();
        services.TryAddSingleton<IIdentityOrganizationDataScopeSqlProjection, IdentityOrganizationDataScopeSqlProjection>();
        services.TryAddScoped<TenantUnits.OrganizationUnitProjectionCatalog>();
        services.TryAddScoped<IIdentityOrganizationUnitProjectionSource>(provider =>
            provider.GetRequiredService<TenantUnits.OrganizationUnitProjectionCatalog>());
        services.TryAddScoped<IWorkflowUnitLeaderDirectory, TenantUnits.WorkflowUnitLeaderDirectory>();
    }

    /// <summary>
    /// 映射 Organization 模块的 HTTP API 端点：租户部门、用户部门隶属、岗位、职级、
    /// 用户岗位以及 Host 用户管理引用接口。
    /// </summary>
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
