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
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Workflow;

/// <summary>提供工作流定义、表单、实例与待办的静态闭包模块入口。</summary>
public sealed class WorkflowModule : IFullNetModule
{
    public string Name => "Workflow";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

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
        services.AddScoped<WorkflowInstanceManagementService>();
        services.AddScoped<WorkflowTodoManagementService>();
#if FULLNET_AOT_COMPILE
        new Persistence.WorkflowDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        FormEndpoint.Map(endpoints);
        FormEndpoint.MapVersion(endpoints);
        DefinitionEndpoint.Map(endpoints);
        DefinitionEndpoint.MapVersion(endpoints);
        InstanceEndpoint.Map(endpoints);
        TodoEndpoint.Map(endpoints);
    }
}
