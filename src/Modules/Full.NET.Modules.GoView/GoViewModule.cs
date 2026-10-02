using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.GoView.Features.ManageProjects;
using Full.NET.Modules.GoView.Features.PreviewProjects;
using Full.NET.Modules.GoView.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.GoView;

/// <summary>提供 GoView 大屏项目草稿保存、发布快照与只读预览 API。</summary>
public sealed class GoViewModule : IFullNetModule
{
    /// <summary>获取 GoView 模块在依赖图中的唯一稳定名称。</summary>
    public string Name => "GoView";

    /// <summary>获取 GoView 模块运行所需的模块依赖；Identity 提供授权目录与身份上下文，Tenancy 提供租户上下文。</summary>
    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
    ];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            GoViewAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<GoViewProjectQueryService>();
        services.TryAddScoped<GoViewProjectManagementService>();
        services.TryAddScoped<GoViewProjectPreviewService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                GoViewJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageProjects.Endpoint.Map(endpoints);
        Features.PreviewProjects.Endpoint.Map(endpoints);
    }
}
