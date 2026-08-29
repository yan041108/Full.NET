using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Configuration;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;
using Full.NET.Modules.ObservabilityAdmin.Resources;
using Full.NET.Modules.ObservabilityAdmin.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin;

/// <summary>提供 Host 运行日志的只读、有界且精确授权的管理控制面。</summary>
public sealed class ObservabilityAdminModule : IFullNetModule
{
    public string Name => "ObservabilityAdmin";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            ObservabilityAdminAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            ObservabilityAdminErrorResourceSource>());
        services.AddOptions<ObservabilityAdminOptions>()
            .Bind(configuration.GetSection(ObservabilityAdminOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<ObservabilityAdminOptions>,
            ObservabilityAdminOptionsValidator>());
        services.TryAddSingleton<LogFileControlPlane>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                ObservabilityAdminJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        Endpoint.Map(endpoints);
}
