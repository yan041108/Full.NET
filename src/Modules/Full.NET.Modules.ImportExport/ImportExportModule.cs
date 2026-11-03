using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.ImportExport.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport;

/// <summary>提供静态 Schema 导入目录、模板下载、预校验与批量执行任务管理。</summary>
public sealed class ImportExportModule : IFullNetModule
{
    /// <summary>获取 ImportExport 模块在依赖图中的唯一稳定名称。</summary>
    public string Name => "ImportExport";

    /// <summary>获取 ImportExport 模块运行所需的模块依赖；Identity 提供授权目录与身份上下文，Files 提供文件存储能力，Tenancy 提供租户上下文。</summary>
    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Files",
        "Tenancy",
    ];

    /// <summary>注册 ImportExport 模块的授权目录、配置校验、时钟、标识生成器以及静态 Schema 与导入任务服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">宿主配置。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.ImportExportDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            ImportExportAuthorizationContributor>());
        AddExecutionServices(services, configuration);
        services.TryAddScoped<Features.ManageImportProfiles.ImportProfileService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErrorResourceSource, global::Full.NET.Modules.ImportExport.Resources.ImportExportErrorResourceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResourceFileOwner,
            Features.ManageImportTasks.ImportExportResourceFileOwner>());
        services.TryAddScoped<Features.BrowseStaticSchemas.StaticImportSchemaQueryService>();
        services.TryAddScoped<Features.ManageImportTasks.ImportExportTaskManagementService>();
        services.TryAddScoped<Features.ManageImportTasks.ImportExportTaskQueryService>();
        services.TryAddScoped<Features.ManageImportTasks.ImportExportTaskExecutionService>();
        services.TryAddScoped<Features.ManageImportTasks.ImportExportTaskCancellationService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                ImportExportJsonSerializerContext.Default));
    }

    /// <summary>
    /// 注册仅由 Worker 承载的导入任务循环，并在 Native AOT 下同步注册行物化器。
    /// </summary>
    /// <param name="services">Worker 宿主服务集合。</param>
    /// <param name="configuration">宿主配置根。</param>
    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.ImportExportDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResourceFileOwner,
            Features.ManageImportTasks.ImportExportResourceFileOwner>());
        AddExecutionServices(services, configuration);
        services.AddHostedService<ImportExportTaskHostedProcessor>();
    }

    /// <summary>API 的同步执行与独立 Worker 使用同一配置和 Schema 目录，避免 Worker 依赖 HTTP 注册副作用。</summary>
    private static void AddExecutionServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ImportExportOptions>()
            .Bind(configuration.GetSection(ImportExportOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ImportExportOptions>, ImportExportOptionsValidator>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<StaticImportSchemaRegistry>();
        services.TryAddScoped<ImportTaskAuthority>();
        services.TryAddSingleton<PendingTenantScanState>();
        services.TryAddScoped<ImportExportTaskRunner>();
    }

    /// <summary>注册 ImportExport 模块的静态 Schema 浏览与导入任务管理 HTTP 端点。</summary>
    /// <param name="endpoints">端点路由构建器。</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.BrowseStaticSchemas.Endpoint.Map(endpoints);
        Features.ManageImportTasks.Endpoint.Map(endpoints);
        Features.ManageImportProfiles.Endpoint.Map(endpoints);
    }
}
