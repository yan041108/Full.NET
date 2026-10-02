using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Cryptography.Configuration;
using Full.NET.Modules.Cryptography.Resources;
using Full.NET.Modules.Cryptography.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Cryptography;

/// <summary>国密控制面模块：提供受控 SM2 签名/验签与密钥状态目录。</summary>
public sealed class CryptographyModule : IFullNetModule
{
    /// <summary>
    /// Cryptography 模块的唯一稳定标识，固定为 "Cryptography"。
    /// </summary>
    public string Name => "Cryptography";

    /// <summary>
    /// Cryptography 模块依赖 Identity 模块，需在其后加载。
    /// </summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    /// <summary>注册 Cryptography 模块的授权目录、错误资源、配置校验以及 SM2 签名/验签与密钥目录服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">宿主配置。</param>
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            CryptographyAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            CryptographyErrorResourceSource>());
        services.AddOptions<CryptographyOptions>()
            .Bind(configuration.GetSection(CryptographyOptions.SectionName))
            .ValidateOnStart();
        services.TryAddScoped<Features.ManageGmKeys.CryptographyStatusService>();
        services.TryAddScoped<Features.ManageGmKeys.CryptographyKeyQueryService>();
        services.TryAddScoped<Features.ManageGmKeys.Sm2SignatureService>();
        services.TryAddScoped<Features.ManageGmKeys.Sm2VerifyService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                CryptographyJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.CryptographyDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>注册 Cryptography 模块的国密密钥管理与 SM2 签名/验签 HTTP 端点。</summary>
    /// <param name="endpoints">端点路由构建器。</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        Features.ManageGmKeys.Endpoint.Map(endpoints);
}
