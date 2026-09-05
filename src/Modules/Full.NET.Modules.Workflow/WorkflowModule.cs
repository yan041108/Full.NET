using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Resources;
using Full.NET.Modules.Workflow.Serialization;
using FormEndpoint = Full.NET.Modules.Workflow.Features.ManageForms.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageForms;
using DefinitionEndpoint = Full.NET.Modules.Workflow.Features.ManageDefinitions.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using InstanceEndpoint = Full.NET.Modules.Workflow.Features.ManageInstances.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using TodoEndpoint = Full.NET.Modules.Workflow.Features.ManageMyTodos.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using CcEndpoint = Full.NET.Modules.Workflow.Features.ManageMyCc.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageMyCc;
using RecoveryEndpoint = Full.NET.Modules.Workflow.Features.ManageRecoveryTasks.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageRecoveryTasks;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow;

/// <summary>提供工作流定义、表单、实例、待办和恢复 Worker 的静态闭包模块入口。</summary>
public sealed class WorkflowModule : IFullNetModule
{
    /// <summary>获取模块稳定名称。</summary>
    public string Name => "Workflow";

    /// <summary>获取 Workflow 运行与可靠通知投影所需的模块依赖。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity", "Notifications", "Organization"];

    /// <summary>注册工作流定义、运行时、抄送及 AOT 静态闭包服务。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    /// <param name="configuration">应用只读配置根。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            WorkflowAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            WorkflowErrorResourceSource>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                WorkflowJsonSerializerContext.Default));
        services.AddScoped<WorkflowFormManagementService>();
        services.AddScoped<WorkflowDefinitionManagementService>();
        services.AddScoped<WorkflowRecipientCandidateQueryService>();
        services.AddScoped<WorkflowRoleCandidateQueryService>();
        services.AddScoped<WorkflowOrganizationUnitCandidateQueryService>();
        services.AddScoped<WorkflowAssigneeResolver>();
        services.AddScoped<WorkflowAssigneePublishValidator>();
        services.AddScoped<WorkflowApprovalAssigneeCoordinator>();
        services.AddScoped<WorkflowInstanceManagementService>();
        services.AddScoped<WorkflowInstanceRecoveryService>();
        services.AddScoped<WorkflowRecoveryTaskService>();
        services.AddScoped<WorkflowTodoManagementService>();
        services.AddScoped<WorkflowTodoCountersignService>();
        services.AddScoped<WorkflowCcTransitionWriter>();
        services.AddScoped<WorkflowParallelJoinCoordinator>();
        services.AddScoped<WorkflowApprovalTransitionExecutor>();
        services.AddScoped<WorkflowAutomaticTransitionWriter>();
        services.AddScoped<WorkflowApprovalActivationWriter>();
        services.AddScoped<WorkflowNotificationOutboxPublisher>();
        services.AddScoped<WorkflowCcManagementService>();
#if FULLNET_AOT_COMPILE
        new Persistence.WorkflowDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>映射工作流表单、定义、实例、待办、抄送和恢复任务端点。</summary>
    /// <param name="endpoints">应用端点路由构建器。</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        FormEndpoint.Map(endpoints);
        FormEndpoint.MapVersion(endpoints);
        DefinitionEndpoint.Map(endpoints);
        DefinitionEndpoint.MapVersion(endpoints);
        InstanceEndpoint.Map(endpoints);
        TodoEndpoint.Map(endpoints);
        CcEndpoint.Map(endpoints);
        RecoveryEndpoint.Map(endpoints);
    }

    /// <summary>只在 Worker 注册恢复扫描与领取循环，避免 API 进程启动全局扫描。</summary>
    /// <param name="services">Worker 宿主服务集合。</param>
    /// <param name="configuration">Worker 只读配置根。</param>
    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
#if FULLNET_AOT_COMPILE
        new Persistence.WorkflowDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        // BindConfiguration 使用配置绑定源生成器，避免 Worker Native AOT 在启动时反射扫描选项类型。
        services.AddOptions<WorkflowRecoveryWorkerOptions>()
            .BindConfiguration(WorkflowRecoveryWorkerOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<WorkflowRecoveryWorkerOptions>,
                WorkflowRecoveryWorkerOptionsValidator>());
        services.AddScoped<WorkflowRecoveryScanner>();
        services.AddScoped<WorkflowRecoveryBatchProcessor>();
        services.AddHostedService<WorkflowRecoveryHostedProcessor>();
        services.AddScoped<WorkflowNotificationOutboxPublisher>();
        services.AddSingleton<WorkflowTodoTimeoutScanCursor>();
        services.AddScoped<WorkflowTodoTimeoutProcessor>();
        services.AddHostedService<WorkflowTodoTimeoutHostedProcessor>();
    }
}
