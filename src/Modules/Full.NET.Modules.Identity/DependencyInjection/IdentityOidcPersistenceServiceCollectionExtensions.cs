using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc.Stores;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Identity.DependencyInjection;

/// <summary>Migrator 与 API 共用静态 Store 闭包，不装配 Cookie、协议服务器或签发密钥。</summary>
internal static class IdentityOidcPersistenceServiceCollectionExtensions
{
    internal static void AddIdentityOidcPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Identity:Oidc:Enable")) return;
        services.AddOptions<IdentityOidcOptions>().Bind(configuration.GetSection(IdentityOidcOptions.SectionName));
        services.AddOptions<IdentityBrowserOptions>().Bind(configuration.GetSection(IdentityBrowserOptions.SectionName));
        services.TryAddScoped<IdentityOidcStoreSqlResolver>();
        services.AddOpenIddict().AddCore(options =>
        {
            // 安全状态不使用进程实体缓存，停用/收紧在下一次协议请求直接读取数据库。
            options.DisableEntityCaching();
            options.SetDefaultApplicationEntity<IdentityOidcApplication>();
            options.SetDefaultAuthorizationEntity<IdentityOidcAuthorization>();
            options.SetDefaultScopeEntity<IdentityOidcScope>();
            options.SetDefaultTokenEntity<IdentityOidcToken>();
            options.ReplaceApplicationStore<IdentityOidcApplication, IdentityOidcApplicationStore>();
            options.ReplaceAuthorizationStore<IdentityOidcAuthorization, IdentityOidcAuthorizationStore>();
            options.ReplaceScopeStore<IdentityOidcScope, IdentityOidcScopeStore>();
            options.ReplaceTokenStore<IdentityOidcToken, IdentityOidcTokenStore>();
        });
    }
}
