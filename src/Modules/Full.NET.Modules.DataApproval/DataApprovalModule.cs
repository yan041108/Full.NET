using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Execution;
using Full.NET.Modules.DataApproval.Features.CrossModulePorts;
using Full.NET.Modules.DataApproval.Features.ManageRequests;
using Full.NET.Modules.DataApproval.Features.ManageScenarios;
using Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;
using Full.NET.Modules.DataApproval.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.DataApproval;

/// <summary>
/// DataApproval 模块入口：编排跨模块变更审批请求，首个切片覆盖 Host 流水号规则更新。
/// 模块只持久化审批快照与工作流关联，不读取其他模块业务表。
/// </summary>
public sealed class DataApprovalModule : IFullNetModule
{
    /// <inheritdoc />
    public string Name => "DataApproval";

    /// <inheritdoc />
    public IReadOnlyCollection<string> Dependencies => ["Identity", "Workflow"];

    /// <inheritdoc />
    public IReadOnlyCollection<string> OptionalContractDependencies => ["SerialNumbers"];

    /// <inheritdoc />
    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            DataApprovalAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<DataApprovalRequestService>();
        services.TryAddScoped<DataApprovalRequestLinkService>();
        services.TryAddScoped<DataApprovalRequestApplicationService>();
        services.TryAddScoped<DataApprovalScenarioService>();
        services.TryAddScoped<DataApprovalSubmissionAdapter>();
        services.TryAddScoped<IDataApprovalScenarioPolicyPort>(
            provider => provider.GetRequiredService<DataApprovalSubmissionAdapter>());
        services.TryAddScoped<IDataApprovalSubmissionPort>(
            provider => provider.GetRequiredService<DataApprovalSubmissionAdapter>());
        services.TryAddScoped<DataApprovalWorkflowOutcomeService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                DataApprovalJsonSerializerContext.Default));
        services.AddOptions<DataApprovalRequestRecoveryWorkerOptions>()
            .Bind(configuration.GetSection(DataApprovalRequestRecoveryWorkerOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<DataApprovalRequestRecoveryWorkerOptions>,
            DataApprovalRequestRecoveryWorkerOptionsValidator>());
        services.AddOptions<DataApprovalRequestApplicationRecoveryWorkerOptions>()
            .Bind(configuration.GetSection(DataApprovalRequestApplicationRecoveryWorkerOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<DataApprovalRequestApplicationRecoveryWorkerOptions>,
            DataApprovalRequestApplicationRecoveryWorkerOptionsValidator>());
    }

    /// <inheritdoc />
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddScoped<DataApprovalRequestLinkService>();
        services.TryAddScoped<DataApprovalRequestApplicationService>();
        services.TryAddScoped<DataApprovalRequestRecoveryBatchProcessor>();
        services.AddHostedService<DataApprovalRequestRecoveryHostedProcessor>();
        services.TryAddScoped<DataApprovalRequestApplicationRecoveryBatchProcessor>();
        services.AddHostedService<DataApprovalRequestApplicationRecoveryHostedProcessor>();
        services.TryAddScoped<DataApprovalWorkflowOutcomeService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IWorkflowInstanceCompletedSink,
            WorkflowInstanceCompletedDataApprovalSink>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IWorkflowInstanceRejectedSink,
            WorkflowInstanceRejectedDataApprovalSink>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IWorkflowInstanceCancelledSink,
            WorkflowInstanceCancelledDataApprovalSink>());
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageRequests.Endpoint.Map(endpoints);
        Features.ManageScenarios.Endpoint.Map(endpoints);
    }
}
