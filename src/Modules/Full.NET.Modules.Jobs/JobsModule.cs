using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Execution.Handlers;
using Full.NET.Modules.Jobs.Resources;
using Full.NET.Modules.Jobs.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs;

public sealed class JobsModule : IFullNetModule
{
    public string Name => "Jobs";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterExecutionCore(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            JobsAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            JobsErrorResourceSource>());
        services.TryAddScoped<Features.ManageHostJobDefinitions.HostJobDefinitionQueryService>();
        services.TryAddScoped<Features.ManageHostJobDefinitions.HostJobDefinitionManagementService>();
        services.TryAddScoped<Features.ManageHostJobExecutions.HostJobExecutionQueryService>();
        services.TryAddScoped<Features.ManageHostJobExecutions.HostJobTriggerService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                JobsJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostJobDefinitions.Endpoint.Map(endpoints);
        Features.ManageHostJobExecutions.Endpoint.Map(endpoints);
    }

    /// <summary>Worker 轮询执行待处理任务；不引入 HTTP 与完整模块依赖图。</summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterExecutionCore(services);
        services.AddOptions<JobsWorkerOptions>()
            .Bind(configuration.GetSection(JobsWorkerOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<JobsWorkerOptions>,
                JobsWorkerOptionsValidator>());
        services.AddHostedService<JobExecutionHostedProcessor>();
    }

    private static void RegisterExecutionCore(IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<JobHandlerRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, PingJobHandler>());
        services.TryAddScoped<JobExecutionRunner>();
    }
}
