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
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.Delivery;
using Full.NET.Modules.Files.Contracts;
using Microsoft.AspNetCore.Builder;
using Full.NET.Modules.EnterpriseRequest.Features.DraftMutations;

namespace Full.NET.Modules.EnterpriseRequest;

public sealed class EnterpriseRequestModule : IFullNetModule
{
    public string Name => "EnterpriseRequest";

    public IReadOnlyCollection<string> Dependencies =>
        ["Identity", "Tenancy", "Organization", "Settings", "Files", "Workflow", "ImportExport", "Reporting", "Printing"];

    public IReadOnlyCollection<string> OptionalContractDependencies => [];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Full.NET.Modules.Settings.Contracts.IQuerySchemeContributor,
            Features.QuerySchemes.EnterpriseRequestQuerySchemeContributor>());
        services.AddSingleton(new WorkflowBusinessTypeOwnership(EnterpriseRequestWorkflowConstants.BusinessType, "enterprise_request"));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            EnterpriseRequestAuthorizationContributor>());
        services.TryAddScoped<SubmitEnterpriseRequestForApprovalService>();
        services.TryAddScoped<Features.Search.EnterpriseRequestSearchService>();
        AddImportServices(services);
        services.AddFullNetGeneratedModuleFeatures();
        AddDeliveryServices(services, configuration);
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0,
            EnterpriseRequestDeliveryJsonContext.Default));
    }

    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        AddImportServices(services);
        AddDeliveryServices(services, configuration);
        services.AddHostedService<EnterpriseRequestDeliveryWorker>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Full.NET.Abstractions.Messaging.IIntegrationEventHandler,
            EnterpriseRequestSubmittedHandler>());
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
        Features.Search.Endpoint.Map(endpoints);
        EnterpriseRequestDeliveryEndpoint.Map(endpoints);
    }

    private static void AddDeliveryServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddScoped<EnterpriseRequestDraftGuard>();
        services.TryAddScoped<IEnterpriseRequestMutationPolicy, EnterpriseRequestMutationPolicy>();
        services.TryAddScoped<IEnterpriseRequestDetailMutationPolicy, EnterpriseRequestDetailMutationPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Full.NET.Hosting.Api.IErrorResourceSource, Resources.EnterpriseRequestDeliveryErrorResourceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationCatalogContributor, EnterpriseRequestAuthorizationContributor>());
        if (!services.Any(d => d.ServiceType == typeof(EnterpriseRequestQueryService))) services.AddFullNetGeneratedModuleFeatures();
        Persistence.EnterpriseRequestDeliveryMaterializer.Register();
        services.TryAddScoped<EnterpriseRequestDeliveryAccess>();
        services.TryAddScoped<EnterpriseRequestDeliverySnapshotReader>();
        services.TryAddScoped<EnterpriseRequestDocumentRenderer>();
        services.TryAddScoped<EnterpriseRequestDeliveryProcessor>();
        services.TryAddScoped<EnterpriseRequestDeliveryService>();
        services.TryAddSingleton<EnterpriseRequestDeliveryScanCursor>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResourceFileOwner, EnterpriseRequestAttachmentFileOwner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResourceFileOwner, EnterpriseRequestReportFileOwner>());
        services.AddOptions<EnterpriseRequestDeliveryOptions>().Bind(configuration.GetSection("EnterpriseRequest:Delivery"));
    }

    // 导入执行发生在独立 Worker；处理器与行幂等账本不得只在 API 中注册。
    private static void AddImportServices(IServiceCollection services)
    {
        services.TryAddScoped<EnterpriseRequestImportWriter>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStaticImportSchemaHandler, EnterpriseRequestStaticImportSchemaHandler>());
    }
}
