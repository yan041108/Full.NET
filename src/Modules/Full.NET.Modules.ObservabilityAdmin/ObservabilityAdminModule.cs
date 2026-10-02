using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Configuration;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;
using Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;
using Full.NET.Modules.ObservabilityAdmin.Features.MonitorServer;
using Full.NET.Modules.ObservabilityAdmin.Resources;
using Full.NET.Modules.ObservabilityAdmin.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin;

/// <summary>提供 Host 运行日志的只读、有界且精确授权的管理控制面。</summary>
public sealed class ObservabilityAdminModule : IFullNetModule
{
    /// <summary>
    /// ObservabilityAdmin 模块的唯一稳定标识，固定为 "ObservabilityAdmin"。
    /// </summary>
    public string Name => "ObservabilityAdmin";

    /// <summary>
    /// ObservabilityAdmin 模块依赖 Identity 模块，需在其后加载。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            ObservabilityAdminAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            ObservabilityAdminErrorResourceSource>());
        services.AddOptions<ObservabilityAdminOptions>()
            .Bind(configuration.GetSection(ObservabilityAdminOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<ObservabilityAdminOptions>,
            ObservabilityAdminOptionsValidator>());
        services.TryAddSingleton<LogFileControlPlane>();
        services.TryAddSingleton<ServerRuntimeReader>();
        services.TryAddSingleton<ServerMonitorService>();
        services.TryAddSingleton<CachePolicyControlPlane>();
        services.AddHttpClient(nameof(ElasticsearchLogPipelineHealthService))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        services.TryAddSingleton<ElasticsearchLogPipelineHealthService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                ObservabilityAdminJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageLogFiles.Endpoint.Map(endpoints);
        Features.MonitorServer.Endpoint.Map(endpoints);
        Features.ManageCachePolicies.Endpoint.Map(endpoints);
        Features.MonitorElasticsearchLogPipeline.Endpoint.Map(endpoints);
    }
}
