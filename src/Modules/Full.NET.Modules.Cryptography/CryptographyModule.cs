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
    public string Name => "Cryptography";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

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

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        Features.ManageGmKeys.Endpoint.Map(endpoints);
}
