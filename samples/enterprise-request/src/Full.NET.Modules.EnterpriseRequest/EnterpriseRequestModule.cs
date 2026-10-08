using Full.NET.Modularity.Modules;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.Printing;
using Full.NET.Modules.EnterpriseRequest.Features.ImportExport;
using Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;
using Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;
using Full.NET.Modules.EnterpriseRequest.Features.ApprovalProgress;
using Full.NET.Modules.EnterpriseRequest.Features.ManageLines;
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
        ["Identity", "Tenancy", "Organization", "Files", "Workflow", "ImportExport", "Printing"];

    public IReadOnlyCollection<string> OptionalContractDependencies => [];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            EnterpriseRequestAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPrintingFormSchemaContributor, EnterpriseRequestPrintingSchemaContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPrintingRecordBindingSource, EnterpriseRequestPrintingBindingSource>());
        services.TryAddScoped<SubmitEnterpriseRequestForApprovalService>();
        services.TryAddScoped<EnterpriseRequestApprovalProgressService>();
        services.TryAddScoped<EnterpriseRequestLineService>();
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, EnterpriseRequestLinesJsonContext.Default));
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, EnterpriseRequestApprovalProgressJsonContext.Default));
        services.TryAddScoped<EnterpriseRequestImportService>();
#if FULLNET_AOT_COMPILE
        Persistence.EnterpriseRequestImportAotMaterializer.Register();
        Persistence.EnterpriseRequestApprovalAotMaterializer.Register();
        Persistence.EnterpriseRequestLineAotMaterializer.Register();
#endif
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IStaticImportSchemaHandler,
            EnterpriseRequestStaticImportSchemaHandler>());
        services.AddFullNetGeneratedModuleFeatures();
    }

    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationCatalogContributor, EnterpriseRequestAuthorizationContributor>());
        // 仅复用生成的业务服务注册，不映射 HTTP 端点；后台使用相同领域授权与事务边界。
        services.AddFullNetGeneratedModuleFeatures();
        services.TryAddScoped<EnterpriseRequestImportService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStaticImportSchemaHandler, EnterpriseRequestStaticImportSchemaHandler>());
#if FULLNET_AOT_COMPILE
        Persistence.EnterpriseRequestImportAotMaterializer.Register();
        Persistence.EnterpriseRequestApprovalAotMaterializer.Register();
        Persistence.EnterpriseRequestLineAotMaterializer.Register();
#endif
        services.TryAddScoped<EnterpriseRequestWorkflowOutcomeService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler, EnterpriseRequestApprovalSubmittedHandler>());
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
        ApprovalProgressEndpoint.Map(endpoints);
        EnterpriseRequestLinesEndpoint.Map(endpoints);
    }
}
