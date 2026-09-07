using System.Text.Json.Serialization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Connectivity;
using Full.NET.Modules.Reporting.Features.BrowseQueryPorts;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Features.ManageDataSources;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Features.ManageGroups;
using Full.NET.Modules.Reporting.Security;
using Full.NET.Modules.Reporting.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting;

/// <summary>提供报表数据源安全配置、连接测试与管理 API。</summary>
public sealed class ReportingModule : IFullNetModule
{
    /// <summary>获取报表模块稳定键。</summary>
    public string Name => "Reporting";

    /// <summary>报表执行与导出依赖身份、文件所有权和租户上下文。</summary>
    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Files",
        "Tenancy",
    ];

    /// <summary>注册报表 HTTP 服务、导出执行器与外部连接适配。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">宿主配置。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.ReportingDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            ReportingAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<ReportingDataSourceSecretProtector>();
        services.TryAddSingleton<ReportingDataSourceConnectionFactory>();
        services.TryAddSingleton<ReportingDataSourceConnectionTester>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResourceFileOwner,
            Features.ManageExportTasks.ReportingResourceFileOwner>());
        services.TryAddScoped<ReportingDataSourceQueryService>();
        services.TryAddScoped<ReportingDataSourceManagementService>();
        services.TryAddScoped<ReportingDataSourceOperationsService>();
        services.TryAddSingleton<ReportingQueryPortQueryService>();
        services.TryAddScoped<ReportingGroupQueryService>();
        services.TryAddScoped<ReportingGroupManagementService>();
        services.TryAddScoped<ReportingDefinitionQueryService>();
        services.TryAddScoped<ReportingDefinitionManagementService>();
        services.TryAddScoped<ReportingDefinitionExecutionService>();
        services.TryAddScoped<ReportingExportTaskQueryService>();
        services.TryAddScoped<IReportingExportWorkbookSource, ReportingExportWorkbookSource>();
        services.TryAddScoped<ReportingExportTaskRunner>();
        services.TryAddScoped<ReportingExportTaskManagementService>();
        services.AddOptions<Configuration.ReportingExportOptions>()
            .Bind(configuration.GetSection(Configuration.ReportingExportOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<Configuration.ReportingExportOptions>,
            Configuration.ReportingExportOptionsValidator>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                ReportingJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageDataSources.Endpoint.Map(endpoints);
        Features.ManageGroups.Endpoint.Map(endpoints);
        Features.BrowseQueryPorts.Endpoint.Map(endpoints);
        Features.ManageDefinitions.Endpoint.Map(endpoints);
        Features.ExecuteDefinitions.Endpoint.Map(endpoints);
        Features.ManageExportTasks.Endpoint.Map(endpoints);
    }

    /// <inheritdoc />
    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.ReportingDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<ReportingDataSourceSecretProtector>();
        services.TryAddSingleton<ReportingDataSourceConnectionFactory>();
        services.TryAddSingleton<ReportingDataSourceConnectionTester>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResourceFileOwner,
            Features.ManageExportTasks.ReportingResourceFileOwner>());
        services.TryAddScoped<ReportingDataSourceQueryService>();
        services.TryAddScoped<ReportingDefinitionQueryService>();
        services.TryAddScoped<ReportingDefinitionExecutionService>();
        services.TryAddScoped<IReportingExportWorkbookSource, ReportingExportWorkbookSource>();
        services.TryAddScoped<ReportingExportTaskRunner>();
        services.AddOptions<Configuration.ReportingExportOptions>()
            .Bind(configuration.GetSection(Configuration.ReportingExportOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<Configuration.ReportingExportOptions>,
            Configuration.ReportingExportOptionsValidator>());
        services.AddHostedService<ReportingExportTaskHostedProcessor>();
    }
}
