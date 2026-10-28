// Workflow 模块宿主入口；注册定义、表单、实例执行与恢复 Worker。
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
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Execution.Connectors;
using Full.NET.Modules.Workflow.Execution.BusinessCommands;
using ConnectorEndpoint = Full.NET.Modules.Workflow.Features.ManageConnectorCalls.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageConnectorCalls;
using Full.NET.Modules.Workflow.Features.ManageRoutes;
using RouteEndpoint = Full.NET.Modules.Workflow.Features.ManageRoutes.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageWorkCalendars;
using CalendarEndpoint = Full.NET.Modules.Workflow.Features.ManageWorkCalendars.Endpoint;
using CcEndpoint = Full.NET.Modules.Workflow.Features.ManageMyCc.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageMyCc;
using RecoveryEndpoint = Full.NET.Modules.Workflow.Features.ManageRecoveryTasks.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageRecoveryTasks;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ProjectWorkflowTerminalEvents;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.CrossModulePorts;
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
    public IReadOnlyCollection<string> Dependencies => ["Files", "Identity", "Notifications", "Organization", "Tenancy"];

    /// <summary>注册工作流定义、运行时、抄送及 AOT 静态闭包服务。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    /// <param name="configuration">应用只读配置根。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(WorkflowConnectorCatalog.FromConfiguration(configuration));
        RegisterConnectorTransport(services);
        Persistence.WorkflowLogTimelineSources.Register(services);
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
        RegisterWorkerRuntimeClosure(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileReferenceClaimProbe,
            WorkflowFormSubmissionAttachmentProbe>());
        services.AddScoped<WorkflowCcManagementService>();
        services.AddScoped<WorkflowStarterSelectionQueryService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostDashboardWorkflowEntryReader,
            HostDashboard.HostDashboardWorkflowEntryReader>());
#if FULLNET_AOT_COMPILE
        new Persistence.WorkflowDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>Worker 恢复、超时与 DataApproval 跨模块启动共享的运行时闭包。</summary>
    private static void RegisterWorkerRuntimeClosure(IServiceCollection services)
    {
        services.AddSingleton(new WorkflowBusinessTypeOwnership(WorkflowChildStartContext.BusinessType, "workflow"));
        services.TryAddSingleton<WorkflowBusinessActionCatalog>();
        services.AddScoped<WorkflowBusinessCommandPublisher>();
        services.AddScoped<WorkflowBusinessCommandActivationWriter>();
        services.AddScoped<WorkflowSubprocessActivationWriter>();
        services.AddScoped<WorkflowRegionEpochCoordinator>();
        services.AddScoped<WorkflowRouteDecisionExecutor>();
        services.AddScoped<WorkflowReturnRoundCoordinator>();
        services.AddScoped<WorkflowMultiInstanceSourcePreparation>();
        services.AddScoped<WorkflowSubprocessStarterPreparation>();
        services.AddScoped<WorkflowSubprocessProcessor>();
        services.TryAddSingleton<WorkflowSubprocessScanCursor>();
        services.AddScoped<IWorkflowBusinessCommandProofReader, WorkflowBusinessCommandProofReader>();
        services.AddScoped<WorkflowBusinessCommandResultService>();
        services.AddScoped<WorkflowBusinessCommandProcessor>();
        services.TryAddSingleton<WorkflowBusinessCommandScanCursor>();
        services.AddScoped<WorkflowFormManagementService>();
        services.AddScoped<WorkflowWorkCalendarService>();
        services.AddScoped<WorkflowDefinitionManagementService>();
        services.AddScoped<WorkflowConnectorManagementService>();
        services.AddScoped<WorkflowRouteManagementService>();
        services.AddScoped<Features.ManageCollaborationGrants.WorkflowCollaborationGrantService>();
        services.AddScoped<WorkflowRecipientCandidateQueryService>();
        services.AddScoped<WorkflowTodoAssigneeCandidateQueryService>();
        services.AddScoped<WorkflowRoleCandidateQueryService>();
        services.AddScoped<WorkflowOrganizationUnitCandidateQueryService>();
        services.AddScoped<WorkflowPositionCandidateQueryService>();
        services.AddScoped<WorkflowAssigneePreviewService>();
        services.AddScoped<WorkflowFormAttachmentCoordinator>();
        services.AddScoped<WorkflowAutomaticOutputCoordinator>();
        services.AddScoped<WorkflowResultCallbackWriter>();
        services.AddScoped<WorkflowHumanActivityCompletionCoordinator>();
        services.AddScoped<WorkflowFormAttachmentAccessService>();
        services.AddScoped<WorkflowAssigneeResolver>();
        services.AddScoped<WorkflowAssigneePublishValidator>();
        services.AddScoped<WorkflowHumanActivityAssigneeCoordinator>();
        services.AddScoped<WorkflowClaimCandidateAuthorizer>();
        services.AddScoped<WorkflowHumanActivityPreparation>();
        services.AddScoped<WorkflowStarterSelectionPreparation>();
        services.AddScoped<WorkflowSignatureCoordinator>();
        services.AddScoped<WorkflowSignatureReadService>();
        services.AddScoped<WorkflowSignatureUploadService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResourceFileOwner, WorkflowTenantFileOwner>());
        services.AddScoped<WorkflowInstanceManagementService>();
        services.AddScoped<WorkflowManualReminderService>();
        services.AddScoped<WorkflowInstanceQueryService>();
        services.AddScoped<WorkflowInstanceRecoveryService>();
        services.AddScoped<WorkflowRecoveryTaskService>();
        services.AddScoped<WorkflowTodoManagementService>();
        services.AddScoped<WorkflowTodoQueryService>();
        services.AddScoped<WorkflowTodoCountersignService>();
        services.AddScoped<WorkflowApprovalStepResolutionCoordinator>();
        services.AddScoped<WorkflowTodoTransferService>();
        services.AddScoped<WorkflowTodoDelegationService>();
        services.AddScoped<WorkflowCcTransitionWriter>();
        services.AddScoped<WorkflowParallelJoinCoordinator>();
        services.AddScoped<WorkflowRuntimeTransitionExecutor>();
        services.AddScoped<WorkflowActivityScheduler>();
        services.AddScoped<WorkflowDelayProcessor>();
        services.AddScoped<WorkflowSlaProcessor>();
        services.AddSingleton<WorkflowSlaScanCursor>();
        services.AddScoped<WorkflowConnectorProcessor>();
        services.AddScoped<WorkflowAsyncBarrierProcessor>();
        services.AddScoped<WorkflowBranchStateCoordinator>();
        services.AddScoped<WorkflowActivityWorkProcessor>();
        services.AddScoped<WorkflowAutomaticTransitionWriter>();
        services.AddScoped<WorkflowHumanActivityActivationWriter>();
        services.AddScoped<WorkflowGroupApprovalDecisionCoordinator>();
        services.AddScoped<WorkflowTaskDecisionCoordinator>();
        services.AddScoped<WorkflowVoteDecisionCoordinator>();
        services.AddScoped<WorkflowVoteAttachmentCoordinator>();
        services.AddScoped<WorkflowVoteTodoService>();
        services.AddScoped<WorkflowClaimPoolService>();
        services.AddScoped<WorkflowClaimReleaseService>();
        services.AddScoped<WorkflowVoteResultWriter>();
        services.AddScoped<WorkflowTodoHandoffWriter>();
        services.AddScoped<WorkflowNotificationOutboxPublisher>();
        services.AddScoped<IWorkflowPublishedDefinitionDirectory, WorkflowPublishedDefinitionDirectoryAdapter>();
        services.AddScoped<IWorkflowInstanceStarter, WorkflowInstanceStarterAdapter>();
        services.AddScoped<IWorkflowInstanceCanceller, WorkflowInstanceCancellerAdapter>();
    }

    /// <summary>映射工作流表单、定义、实例、待办、抄送和恢复任务端点。</summary>
    /// <param name="endpoints">应用端点路由构建器。</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        FormEndpoint.Map(endpoints);
        FormEndpoint.MapVersion(endpoints);
        FormEndpoint.MapVersionMutations(endpoints);
        DefinitionEndpoint.Map(endpoints);
        DefinitionEndpoint.MapVersion(endpoints);
        DefinitionEndpoint.MapVersionMutations(endpoints);
        InstanceEndpoint.Map(endpoints);
        TodoEndpoint.Map(endpoints);
        WorkflowClaimEndpoint.Map(endpoints);
        CcEndpoint.Map(endpoints);
        RecoveryEndpoint.Map(endpoints);
        ConnectorEndpoint.Map(endpoints);
        RouteEndpoint.Map(endpoints);
        Features.ManageCollaborationGrants.Endpoint.Map(endpoints);
        CalendarEndpoint.Map(endpoints);
    }

    /// <summary>只在 Worker 注册恢复扫描与领取循环，避免 API 进程启动全局扫描。</summary>
    /// <param name="services">Worker 宿主服务集合。</param>
    /// <param name="configuration">Worker 只读配置根。</param>
    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(WorkflowConnectorCatalog.FromConfiguration(configuration));
        RegisterConnectorTransport(services);
#if FULLNET_AOT_COMPILE
        new Persistence.WorkflowDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
        RegisterWorkerRuntimeClosure(services);
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
        services.AddHostedService<WorkflowConnectorHostedProcessor>();
        services.AddHostedService<WorkflowAsyncBarrierHostedProcessor>();
        services.AddHostedService<WorkflowSlaHostedProcessor>();
        services.AddHostedService<WorkflowBusinessCommandHostedProcessor>();
        services.AddHostedService<WorkflowSubprocessHostedProcessor>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler, WorkflowBusinessCommandRequestedHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler, WorkflowBusinessCommandResultHandler>());
        services.AddScoped<WorkflowNotificationOutboxPublisher>();
        services.AddSingleton<WorkflowTodoTimeoutScanCursor>();
        services.AddScoped<WorkflowTodoTimeoutProcessor>();
        services.AddHostedService<WorkflowTodoTimeoutHostedProcessor>();
        services.AddScoped<WorkflowVoteDeadlineProcessor>();
        services.AddHostedService<WorkflowVoteDeadlineHostedProcessor>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResourceFileOwner, WorkflowTenantFileOwner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileReferenceClaimProbe,
            WorkflowFormSubmissionAttachmentProbe>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceCompletedIntegrationEventHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceRejectedIntegrationEventHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceCancelledIntegrationEventHandler>());
    }

    /// <summary>传输层只允许受保护命名客户端；凭据从部署环境引用解析，绝不来自流程JSON。</summary>
    private static void RegisterConnectorTransport(IServiceCollection services)
    {
        services.AddHttpClient("fullnet.workflow.connector", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(WorkflowConnectorHttpClient.CreateProtectedHandler);
        services.TryAddSingleton(provider => new WorkflowConnectorHttpClient(
            _ => provider.GetRequiredService<IHttpClientFactory>().CreateClient("fullnet.workflow.connector"),
            reference => reference.StartsWith("env:", StringComparison.Ordinal) ? Environment.GetEnvironmentVariable(reference[4..]) : null));
    }
}
