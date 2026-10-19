using Full.NET.Hosting.Api;
using Full.NET.Abstractions.Observability;
using Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;
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
    /// Identity 提供授权，Settings 提供管理端同步调用的 Grid 偏好失效契约。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity", "Settings"];

    /// <summary>
    /// 注册 ObservabilityAdmin 模块的应用服务：授权目录、错误资源、ObservabilityAdmin 选项校验、
    /// 日志文件控制面、服务器运行时读取与监控服务、缓存策略控制面以及 Elasticsearch 日志管道健康检查。
    /// </summary>
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
        services.TryAddSingleton(provider => new LogFileControlPlane(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObservabilityAdminOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
            configuration.GetSection(Full.NET.Hosting.Observability.LoggingOptions.SectionName)
                .Get<Full.NET.Hosting.Observability.LoggingOptions>() ?? new()));
        services.AddOptions<RuntimeLogTimelineOptions>()
            .Bind(configuration.GetSection(RuntimeLogTimelineOptions.SectionName))
            .Validate(value => !value.ElasticsearchEnabled || ElasticsearchTimelineReader.ValidConfiguration(value),
                "Timeline Elasticsearch requires a credential-free HTTPS root URI and fn-logs- index pattern.")
            .ValidateOnStart();
        services.AddScoped<ILogTimelineReader, LocalFileTimelineReader>();
        services.AddScoped<ILogTimelineReader, ElasticsearchTimelineReader>();
        services.AddHttpClient(nameof(ElasticsearchTimelineReader))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(2))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.TryAddSingleton<ServerRuntimeReader>();
        services.TryAddSingleton<ServerMonitorService>();
        services.TryAddSingleton<CachePolicyControlPlane>();
        services.TryAddSingleton<ElasticsearchLogPipelineHealthService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                ObservabilityAdminJsonSerializerContext.Default));
    }

    /// <summary>
    /// 映射 ObservabilityAdmin 模块的 HTTP API 端点：日志文件管理、服务器运行时监控、
    /// 缓存策略管理以及 Elasticsearch 日志管道健康检查接口。
    /// </summary>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageLogFiles.Endpoint.Map(endpoints);
        Features.MonitorServer.Endpoint.Map(endpoints);
        Features.ManageCachePolicies.Endpoint.Map(endpoints);
        Features.MonitorElasticsearchLogPipeline.Endpoint.Map(endpoints);
    }
}
