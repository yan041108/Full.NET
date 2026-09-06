using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport;

/// <summary>提供静态 Schema 导入目录、模板下载与预校验任务管理。</summary>
public sealed class ImportExportModule : IFullNetModule
{
    public string Name => "ImportExport";

    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Files",
        "Tenancy",
    ];

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
        services.AddOptions<ImportExportOptions>()
            .Bind(configuration.GetSection(ImportExportOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<ImportExportOptions>,
            ImportExportOptionsValidator>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<StaticImportSchemaRegistry>();
        services.TryAddScoped<Features.BrowseStaticSchemas.StaticImportSchemaQueryService>();
        services.TryAddScoped<Features.ManageImportTasks.ImportExportTaskManagementService>();
        services.TryAddScoped<Features.ManageImportTasks.ImportExportTaskQueryService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                ImportExportJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.BrowseStaticSchemas.Endpoint.Map(endpoints);
        Features.ManageImportTasks.Endpoint.Map(endpoints);
    }
}
