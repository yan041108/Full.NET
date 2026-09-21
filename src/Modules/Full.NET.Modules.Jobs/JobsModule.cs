using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Jobs.Middleware;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Execution.Handlers;
using Full.NET.Modules.Jobs.Resources;
using Full.NET.Modules.Jobs.Scheduling;
using Full.NET.Modules.Jobs.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

namespace Full.NET.Modules.Jobs;

/// <summary>
/// Jobs 业务模块入口。注册 Host 任务定义（JobDefinition）、调度计划（JobSchedule，Cron/一次性）、
/// 执行记录（JobExecution）的管理与只读查询服务、Cron 调度计算与分发器、按 HandlerKind 解析的内置执行器、
/// 手动触发服务，并映射定义/计划/执行三类端点。
/// AddServices 仅装配查询与管理；AddBackgroundServices（仅 Worker）额外装配 JobsWorkerOptions、
/// JobExecutionHostedProcessor 轮询 BackgroundService（到期调度派发 + 待处理执行 + 积压采样可观测）。
/// 依赖 Identity 模块提供授权目录，并通过 Settings Contract Port 解析敏感配置引用。
/// </summary>
/// <remarks>
/// 依赖 Identity 提供授权目录，Settings Contract Port 解析敏感配置引用；
/// AddServices 仅装配查询与管理，后台轮询（JobExecutionHostedProcessor）只在 Worker AddBackgroundServices 注册，避免 API 进程重复领取。
/// </remarks>
public sealed class JobsModule : IFullNetModule
{
    /// <summary>获取 Jobs 业务模块名称。</summary>
    public string Name => "Jobs";

    /// <summary>获取 Jobs 模块所需依赖；Identity 提供授权目录，Settings Contract Port 解析敏感配置引用。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity", "Settings"];

    /// <summary>
    /// 注册 Host 任务定义、调度计划、执行记录的查询与管理服务、Cron 计算分发器、按 HandlerKind 解析的内置执行器与手动触发服务；
    /// 仅装配查询与管理，不启动后台轮询。
    /// </summary>
    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterExecutionCore(services, configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            JobsAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            JobsErrorResourceSource>());
        services.TryAddScoped<Features.ManageHostJobDefinitions.HostJobDefinitionQueryService>();
        services.TryAddScoped<Features.ManageHostJobDefinitions.HostJobDefinitionManagementService>();
        services.TryAddScoped<Features.ManageHostJobExecutions.HostJobExecutionQueryService>();
        services.TryAddScoped<Features.ManageHostJobExecutions.HostJobExecutionCancelService>();
        services.TryAddScoped<Features.ManageHostJobExecutions.HostJobTriggerService>();
        services.TryAddScoped<Features.ManageHostJobSchedules.HostJobScheduleService>();
        services.TryAddScoped<Features.ManageHostJobHealth.HostJobHealthQueryService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                JobsJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.JobsDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>映射 Jobs 模块定义、计划、执行与健康检查的全部受保护 HTTP 路由。</summary>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostJobDefinitions.Endpoint.Map(endpoints);
        Features.ManageHostJobExecutions.Endpoint.Map(endpoints);
        Features.ManageHostJobSchedules.Endpoint.Map(endpoints);
        Features.ManageHostJobHealth.Endpoint.Map(endpoints);
    }

    /// <inheritdoc />
    public void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
    {
        if (stage == ModulePipelineStage.BeforeEndpoints)
        {
            app.UseMiddleware<HostJobsHostContextMiddleware>();
        }
    }

    /// <summary>Worker 轮询执行待处理任务；不引入 HTTP 与完整模块依赖图。</summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
#if FULLNET_AOT_COMPILE
        new Persistence.JobsDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        RegisterExecutionCore(services, configuration);
        services.AddOptions<JobsWorkerOptions>()
            .Bind(configuration.GetSection(JobsWorkerOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<JobsWorkerOptions>,
                JobsWorkerOptionsValidator>());
        services.AddHostedService<JobExecutionHostedProcessor>();
        services.TryAddSingleton<JobWorkerHeartbeatService>();
        services
            .AddOpenTelemetry()
            .WithMetrics(metrics =>
                metrics.AddMeter(JobsTelemetry.MeterName));
    }

    private static void RegisterExecutionCore(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JobsWorkerOptions>();
        services.AddOptions<JobsHttpOptions>()
            .Bind(configuration.GetSection(JobsHttpOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<JobsHttpOptions>,
                JobsHttpOptionsValidator>());
        services.AddHttpClient(HttpJobExecutor.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                JobsHttpMessageHandlerFactory.Create(
                    serviceProvider.GetRequiredService<IOptions<JobsHttpOptions>>()));
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<
            IJobsRetryJitterSource,
            SystemJobsRetryJitterSource>();
        services.TryAddScoped<JobHandlerKindRegistry>();
        services.TryAddScoped<JobsBacklogReader>();
        services.TryAddScoped<JobScheduleDispatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IJobHandlerExecutor, PingJobExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IJobHandlerExecutor, HttpJobExecutor>());
        services.TryAddScoped<JobExecutionRunner>();
    }
}
