using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.DataApproval.Features.ManageRequests;
using Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;
using Full.NET.Modules.DataApproval.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public IReadOnlyCollection<string> Dependencies => ["Identity", "Workflow", "SerialNumbers"];

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
        services.TryAddScoped<DataApprovalWorkflowOutcomeService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceCompletedDataApprovalHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceRejectedDataApprovalHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceCancelledDataApprovalHandler>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                DataApprovalJsonSerializerContext.Default));
    }

    /// <inheritdoc />
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddScoped<DataApprovalWorkflowOutcomeService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceCompletedDataApprovalHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceRejectedDataApprovalHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            WorkflowInstanceCancelledDataApprovalHandler>());
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        Endpoint.Map(endpoints);
}
