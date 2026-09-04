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
using CcEndpoint = Full.NET.Modules.Workflow.Features.ManageMyCc.Endpoint;
using Full.NET.Modules.Workflow.Features.ManageMyCc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Workflow;

/// <summary>提供工作流定义、表单、实例与待办的静态闭包模块入口。</summary>
public sealed class WorkflowModule : IFullNetModule
{
    /// <summary>获取模块稳定名称。</summary>
    public string Name => "Workflow";

    /// <summary>获取 Workflow 运行所需的模块依赖。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

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
        services.AddScoped<WorkflowInstanceManagementService>();
        services.AddScoped<WorkflowTodoManagementService>();
        services.AddScoped<WorkflowCcTransitionWriter>();
        services.AddScoped<WorkflowCcManagementService>();
#if FULLNET_AOT_COMPILE
        new Persistence.WorkflowDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>映射工作流表单、定义、实例、待办和抄送端点。</summary>
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
    }
}
