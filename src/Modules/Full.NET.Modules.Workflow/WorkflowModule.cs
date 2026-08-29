using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Resources;
using Full.NET.Modules.Workflow.Serialization;
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
#if FULLNET_AOT_COMPILE
        new Persistence.WorkflowDapperAotMaterializerContributor()
            .RegisterMaterializers(
                new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // 首个领域切片尚未发布 HTTP 端点，入口保留显式空实现以维持模块静态闭包。
    }
}
