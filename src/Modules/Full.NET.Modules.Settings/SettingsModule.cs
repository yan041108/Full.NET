using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Observability;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;
using Full.NET.Modules.Settings.Features.ManageHostDictTypes;
using Full.NET.Modules.Settings.Persistence;
using Full.NET.Modules.Settings.Resources;
using Full.NET.Modules.Settings.Seeding;
using Full.NET.Modules.Settings.Serialization;
using Full.NET.Seeding.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Settings;

/// <summary>
/// Settings 业务模块入口。注册系统参数配置、Host/Tenant 双作用域数据字典、枚举目录与用户网格偏好等领域服务，
/// 提供配置项 CRUD、字典类型与项管理（Host/Tenant 隔离）、枚举目录查询、网格偏好读写、诊断策略等功能，
/// 并映射对应 API 端点。依赖 Identity 模块提供授权目录与身份上下文。
/// </summary>
public sealed class SettingsModule : IFullNetModule
{
    public string Name => "Settings";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddMigrationServices(services, configuration);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            SettingsAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IEnumCatalogContributor,
            Catalogs.SettingsEnumCatalogContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IEnumCatalogContributor,
            Catalogs.IdentityAccountTypeEnumCatalogContributor>());
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
        services.TryAddScoped<
            ITransactionalDomainAuditWriter<DiagnosticPolicyAuditWrite>,
            DiagnosticPolicyAuditWriter>();
        services.TryAddScoped<DiagnosticPolicyCacheInvalidator>();
        services.TryAddScoped<DiagnosticPolicyManagementService>();
        services.RemoveAll<IDiagnosticPolicyStore>();
        services.AddSingleton<IDiagnosticPolicyStore, DiagnosticPolicyStore>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                SettingsJsonSerializerContext.Default));
    }

    public void AddMigrationServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDataSeedContributor,
            HostUserProfileDictionarySeedContributor>());
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
        Features.ManageDiagnosticPolicy.Endpoint.Map(endpoints);
    }
}
