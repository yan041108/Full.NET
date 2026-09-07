using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.K3Cloud.Connectivity;
using Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;
using Full.NET.Modules.K3Cloud.Features.ManageDocumentSyncs;
using Full.NET.Modules.K3Cloud.Security;
using Full.NET.Modules.K3Cloud.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.K3Cloud;

/// <summary>提供金蝶 K3Cloud ValidateUser 连接测试与固定销售订单 Save/Submit 同步 API。</summary>
public sealed class K3CloudModule : IFullNetModule
{
    public string Name => "K3Cloud";

    public IReadOnlyCollection<string> Dependencies =>
    [
        "Identity",
        "Tenancy",
    ];

    /// <summary>注册 K3Cloud 模块服务，远程客户端通过接口暴露以便事务外调用可测试。</summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="configuration">宿主配置。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            K3CloudAuthorizationContributor>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<K3CloudPasswordProtector>();
        services.TryAddSingleton<K3CloudWebApiClient>();
        services.TryAddSingleton<IK3CloudWebApiClient>(static provider =>
            provider.GetRequiredService<K3CloudWebApiClient>());
        services.TryAddScoped<K3CloudConnectionQueryService>();
        services.TryAddScoped<K3CloudConnectionManagementService>();
        services.TryAddScoped<K3CloudConnectionOperationsService>();
        services.TryAddScoped<K3CloudDocumentSyncQueryService>();
        services.TryAddScoped<K3CloudDocumentSyncService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                K3CloudJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageConnectionConfigs.Endpoint.Map(endpoints);
        Features.ManageDocumentSyncs.Endpoint.Map(endpoints);
    }
}
