using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Regions.Resources;
using Full.NET.Modules.Regions.Seeding;
using Full.NET.Modules.Regions.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Seeding.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Regions;

/// <summary>
/// 行政区域模块：提供 Host 级行政区划参考数据、级联查询与可审查的数据集导入能力。
/// </summary>
public sealed class RegionsModule : IFullNetModule
{
    /// <summary>获取行政区域模块名称。</summary>
    public string Name => "Regions";

    /// <summary>获取行政区域模块运行所需的模块依赖。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    /// <summary>注册行政区域模块服务、授权目录与 JSON 源生成上下文。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    /// <param name="configuration">宿主配置根。</param>
    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddMigrationServices(services, configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            RegionsAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            RegionsErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<Features.ManageAdministrativeRegions.AdministrativeRegionQueryService>();
        services.TryAddScoped<Features.ManageAdministrativeRegions.AdministrativeRegionManagementService>();
        services.TryAddScoped<Features.ManageAdministrativeRegions.AdministrativeRegionImportService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                RegionsJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.RegionsDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>注册迁移与种子贡献者所需的最小服务。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    /// <param name="configuration">宿主配置根。</param>
    public void AddMigrationServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDataSeedContributor,
            RegionsAdministrativeBaselineSeedContributor>());
    }

    /// <summary>映射行政区域模块全部 HTTP 路由。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageAdministrativeRegions.Endpoint.Map(endpoints);
    }
}
