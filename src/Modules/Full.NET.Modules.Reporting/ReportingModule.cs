using System.Text.Json.Serialization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.BrowseQueryPorts;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Features.ManageDataSources;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.ManageGroups;
using Full.NET.Modules.Reporting.Security;
using Full.NET.Modules.Reporting.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Reporting;

/// <summary>提供报表数据源安全配置、连接测试与管理 API。</summary>
public sealed class ReportingModule : IFullNetModule
{
    public string Name => "Reporting";

    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
    ];

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
        services.TryAddScoped<ReportingDataSourceQueryService>();
        services.TryAddScoped<ReportingDataSourceManagementService>();
        services.TryAddScoped<ReportingDataSourceOperationsService>();
        services.TryAddSingleton<ReportingQueryPortQueryService>();
        services.TryAddScoped<ReportingGroupQueryService>();
        services.TryAddScoped<ReportingGroupManagementService>();
        services.TryAddScoped<ReportingDefinitionQueryService>();
        services.TryAddScoped<ReportingDefinitionManagementService>();
        services.TryAddScoped<ReportingDefinitionExecutionService>();
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
    }
}
