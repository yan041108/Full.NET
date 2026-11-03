using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http;

namespace Full.NET.Modules.Identity.DependencyInjection;

/// <summary>在现有 API 中装配静态 BFF 闭包；不新增宿主，不启用 EF 或无状态回调。</summary>
internal static class IdentityBrowserServiceCollectionExtensions
{
    internal static void AddIdentityBrowser(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityBrowserOptions>();
        services.TryAddSingleton<IIdentityBrowserSession, NoIdentityBrowserSession>();
        var options = configuration.GetSection(IdentityBrowserOptions.SectionName).Get<IdentityBrowserOptions>() ?? new();
        if (!options.Enable) return;
        var oidc = configuration.GetSection(IdentityOidcOptions.SectionName).Get<IdentityOidcOptions>() ?? new();
        IdentityBrowserOptionsValidation.Validate(options, oidc);
        if (configuration["Identity:Issuer"] == oidc.Issuer)
            throw new InvalidOperationException("Browser OIDC and legacy token issuers must be distinct.");
        // 显式启用 BFF 后收敛人类认证信任，不让旧密码会话或 legacy OAuth 回调绕过 MFA。
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure(jwt =>
        {
            jwt.TokenValidationParameters.ValidIssuer = oidc.Issuer;
            jwt.TokenValidationParameters.ValidIssuers = [oidc.Issuer];
        });
        static SymmetricSecurityKey Key(string text, string id)
        {
            if (!Convert.TryFromBase64String(text, new byte[32], out var count) || count != 32)
                throw new InvalidOperationException("Browser state keys must be independent persistent 256-bit Base64 keys.");
            return new SymmetricSecurityKey(Convert.FromBase64String(text)) { KeyId = id };
        }
        if (options.StateSigningKeyBase64 == options.StateEncryptionKeyBase64
            || options.StateEncryptionKeyBase64 == oidc.EncryptionKeyBase64
            || options.StateSigningKeyBase64 == oidc.EncryptionKeyBase64)
            throw new InvalidOperationException("Browser and authorization server protection keys must be distinct.");
        services.AddScoped<IdentityBrowserSession>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, IdentityProtocolHttpSafetyFilter>());
        services.Replace(ServiceDescriptor.Scoped<IIdentityBrowserSession>(provider => provider.GetRequiredService<IdentityBrowserSession>()));
        services.AddOpenIddict().AddClient(client =>
        {
            client.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
            client.SetRedirectionEndpointUris("/api/v1/identity/browser/callback");
            client.AddSigningKey(Key(options.StateSigningKeyBase64, "browser-state-signing"));
            client.AddEncryptionKey(Key(options.StateEncryptionKeyBase64, "browser-state-encryption"));
            client.AddEventHandler<OpenIddictClientEvents.ValidateTokenContext>(handler => handler
                .SetOrder(OpenIddictClientHandlers.Protection.ResolveTokenValidationParameters.Descriptor.Order + 500)
                .UseInlineHandler(context =>
                {
                    // 授权码通道的组件默认可省略 ID Token 有效期；本 BFF 明确要求仍在有效期内。
                    if (context.ValidTokenTypes.Contains(OpenIddict.Abstractions.OpenIddictConstants.TokenTypeIdentifiers.IdentityToken))
                    {
                        context.TokenValidationParameters.ValidateLifetime = true;
                        context.TokenValidationParameters.RequireExpirationTime = true;
                    }
                    return default;
                }));
            client.UseAspNetCore().EnableRedirectionEndpointPassthrough();
            // 刷新请求不能自动重放：上次响应丢失时无法判断旧 refresh 是否已消费。
            client.UseSystemNetHttp().SetHttpErrorPolicy(Polly.Policy.NoOpAsync<HttpResponseMessage>())
                .ConfigureHttpClient(http => http.Timeout = TimeSpan.FromSeconds(10));
            var registration = new OpenIddictClientRegistration
            {
                RegistrationId = IdentityBrowserSession.RegistrationId, Issuer = new Uri(oidc.Issuer),
                ConfigurationEndpoint = new Uri(oidc.Issuer.TrimEnd('/') + "/.well-known/openid-configuration"),
                ClientId = options.ClientId, ClientSecret = options.ClientSecret,
                RedirectUri = new Uri(options.PublicOrigin + "/api/v1/identity/browser/callback"),
                Scopes = { "openid", "profile", "offline_access" }
            };
            registration.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            client.AddRegistration(registration);
        });
    }
}
