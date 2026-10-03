using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion.Serialization;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Observability;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Auditing;
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
    /// <summary>获取 Settings 模块在依赖图中的唯一稳定名称。</summary>
    public string Name => "Settings";

    /// <summary>获取 Settings 模块运行所需的模块依赖；Identity 提供授权目录与身份上下文。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    /// <summary>
    /// 注册 Settings 模块的应用服务：系统参数配置、Host/Tenant 双作用域数据字典、枚举目录、
    /// 网格偏好与诊断策略等领域服务，以及授权目录、错误资源、缓存 JSON 类型信息与 AOT 物化器。
    /// </summary>
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            ICacheJsonTypeInfoContributor,
            SettingsCacheJsonTypeInfoContributor>());
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
        services.TryAddScoped<ISettingsSecretValueResolver, Features.ManageHostConfigEntries.SettingsSecretValueResolver>();
        services.TryAddScoped<Features.QueryHostEnumCatalogs.HostEnumCatalogQueryService>();
        services.TryAddScoped<Features.QueryHostEnumCatalogs.HostEnumCatalogDictGenerationService>();
        services.TryAddScoped<
            Features.ManageMyGridPreferences.MyGridPreferenceService>();
        services.TryAddScoped<
            ITransactionalDomainAuditWriter<DiagnosticPolicyAuditWrite>,
            DiagnosticPolicyAuditWriter>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDomainAuditChangeDiffReader,
            SettingsDomainAuditChangeDiffReader>());
        services.TryAddScoped<DiagnosticPolicyCacheInvalidator>();
        services.TryAddScoped<DiagnosticPolicyManagementService>();
        services.RemoveAll<IDiagnosticPolicyStore>();
        services.TryAddSingleton<DiagnosticPolicyStore>();
        services.AddSingleton<IDiagnosticPolicyStore>(provider =>
            provider.GetRequiredService<DiagnosticPolicyStore>());
        services.AddHostedService<DiagnosticPolicyRefreshService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                SettingsJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.SettingsDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>
    /// 注册 Migrator 执行 Settings 种子数据所需的最小服务集合：时钟、ID 生成器与 Host 用户档案字典种子贡献者。
    /// </summary>
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

    /// <summary>
    /// 映射 Settings 模块的 HTTP API 端点：Host/Tenant 字典类型与项、Host 配置项、
    /// 枚举目录查询、用户网格偏好与诊断策略管理接口。
    /// </summary>
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

    /// <summary>注册 Worker 解析 secret 配置项所需的最小 Settings Port。</summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.SettingsDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddScoped<ISettingsSecretValueResolver, Features.ManageHostConfigEntries.SettingsSecretValueResolver>();
    }
}
