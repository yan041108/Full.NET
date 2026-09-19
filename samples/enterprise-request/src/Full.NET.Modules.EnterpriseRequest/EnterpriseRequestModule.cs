using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.ImportExport;
using Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;
using Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Full.NET.Modules.EnterpriseRequest.Generated;

namespace Full.NET.Modules.EnterpriseRequest;

public sealed class EnterpriseRequestModule : IFullNetModule
{
    public string Name => "EnterpriseRequest";

    public IReadOnlyCollection<string> Dependencies =>
        ["Identity", "Tenancy", "Organization", "Files", "Workflow", "ImportExport"];

    public IReadOnlyCollection<string> OptionalContractDependencies => [];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            EnterpriseRequestAuthorizationContributor>());
        services.TryAddScoped<SubmitEnterpriseRequestForApprovalService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IStaticImportSchemaHandler,
            EnterpriseRequestStaticImportSchemaHandler>());
        services.AddFullNetGeneratedModuleFeatures();
    }

    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddScoped<EnterpriseRequestWorkflowOutcomeService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IWorkflowInstanceCompletedSink,
            WorkflowInstanceCompletedEnterpriseRequestSink>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IWorkflowInstanceRejectedSink,
            WorkflowInstanceRejectedEnterpriseRequestSink>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IWorkflowInstanceCancelledSink,
            WorkflowInstanceCancelledEnterpriseRequestSink>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFullNetGeneratedModuleFeatures();
        SubmitForApprovalEndpoint.Map(endpoints);
    }
}