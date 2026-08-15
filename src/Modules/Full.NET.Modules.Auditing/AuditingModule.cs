using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Features.WriteOutboundCallLogs;
using Full.NET.Modules.Auditing.Middleware;
using Full.NET.Modules.Auditing.Retention;
using Full.NET.Modules.Auditing.Resources;
using Full.NET.Modules.Auditing.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

namespace Full.NET.Modules.Auditing;

/// <summary>
/// Auditing 业务模块入口。注册操作/异常/访问/出站调用四类审计日志的写入缓冲（B0 同事务/B1 异步有界 Channel/B2 Fire-and-Forget 三可靠性分级）、
/// 游标分页只读查询、审计保留策略后台清理服务、中间件管道（AuditWriteCoordinator→Operation→Exception），
/// 并映射查询端点与环境探针端点。依赖 Identity 模块提供授权目录。
/// 仅在 Worker AddBackgroundServices 中装配保留清理 BackgroundService，避免 API 进程重复执行。
/// </summary>
public sealed class AuditingModule : IFullNetModule
{
    public string Name => "Auditing";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuditingQueryOptions>()
            .Bind(configuration.GetSection(AuditingQueryOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AuditingQueryOptions>,
            AuditingQueryOptionsValidator>());
        services.AddOptions<AuditMicroBatchOptions>()
            .Bind(configuration.GetSection(AuditMicroBatchOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AuditMicroBatchOptions>,
            AuditMicroBatchOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            AuditingAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            AuditingErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        // 目录在模块注册阶段以实例形式立即构造，重复 ActionKey 会在宿主启动时直接抛出。
        services.TryAddSingleton(AuditReliabilityCatalog.CreateDefault());
        services.TryAddScoped<IAuditWriteCapturePolicy, CaptureAllAuditWritesPolicy>();
        services.TryAddScoped<AuditWriteBuffer>();
        services.TryAddScoped<AuditWriteBatchWriter>();
        // B1 协调器必须单例：Middleware/Outbound 与 HostedService 共享同一有界 Channel。
        services.TryAddSingleton<AuditMicroBatchCoordinator>();
        services.AddHostedService(provider =>
            provider.GetRequiredService<AuditMicroBatchCoordinator>());
        services.TryAddScoped<OperationLogWriter>();
        services.TryAddScoped<ExceptionLogWriter>();
        services.TryAddScoped(provider => new OutboundCallAuditHandler(
            provider.GetRequiredService<AuditMicroBatchCoordinator>(),
            provider.GetRequiredService<IIdGenerator>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ILogger<OutboundCallAuditHandler>>()));
        services.TryAddSingleton<AuditingContainsTimeRangePolicy>();
        services.TryAddScoped<Features.QueryHostAccessLogs.HostAccessLogQueryService>();
        services.TryAddScoped<Features.QueryHostOperationLogs.HostOperationLogQueryService>();
        services.TryAddScoped<Features.QueryHostExceptionLogs.HostExceptionLogQueryService>();
        services.TryAddScoped<Features.QueryHostOutboundCallLogs.HostOutboundCallLogQueryService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostDashboardAuditMetricsReader,
            HostDashboard.HostDashboardAuditMetricsReader>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                AuditingJsonSerializerContext.Default));
        services
            .AddOpenTelemetry()
            .WithMetrics(metrics =>
                metrics.AddMeter(AuditMicroBatchTelemetry.MeterName));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.QueryHostAccessLogs.Endpoint.Map(endpoints);
        Features.QueryHostOperationLogs.Endpoint.Map(endpoints);
        Features.QueryHostExceptionLogs.Endpoint.Map(endpoints);
        Features.QueryHostOutboundCallLogs.Endpoint.Map(endpoints);
        var environment = endpoints.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        Features.TriggerExceptionProbe.Endpoint.Map(endpoints, environment);
        Features.TriggerOutboundCallProbe.Endpoint.Map(endpoints, environment);
    }

    /// <summary>
    /// 仅为 Worker 装配默认关闭的审计保留清理，避免 API 进程重复执行后台任务。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuditingRetentionOptions>()
            .Bind(configuration.GetSection(AuditingRetentionOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AuditingRetentionOptions>,
            AuditingRetentionOptionsValidator>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<AuditingRetentionRunner>();
        services.AddHostedService<AuditingRetentionHostedProcessor>();
        services
            .AddOpenTelemetry()
            .WithMetrics(metrics =>
                metrics.AddMeter(AuditingRetentionTelemetry.MeterName));
    }

    /// <summary>
    /// 异常日志中间件必须最靠近 Endpoint，以便捕获业务异常后重抛给外层 ExceptionHandler。
    /// </summary>
    public void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
    {
        if (stage == ModulePipelineStage.BeforeEndpoints)
        {
            app.UseMiddleware<AuditWriteCoordinatorMiddleware>();
            app.UseMiddleware<OperationLogMiddleware>();
            app.UseMiddleware<ExceptionLogMiddleware>();
        }
    }
}
