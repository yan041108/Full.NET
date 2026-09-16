using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Features.ManageOidcAuthorizations;
using Full.NET.Modules.Identity.Features.ManageOidcClients;
using Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;
using Full.NET.Modules.Identity.Retention;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Oidc.Stores;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.DependencyInjection;

internal static class IdentityOidcServiceCollectionExtensions
{
    internal static IServiceCollection AddIdentityOidc(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(IdentityOidcRegistrationMarker)))
        {
            return services;
        }

        services.TryAddSingleton<IdentityOidcRegistrationMarker>();
        services.TryAddScoped<IdentityOidcSessionService>();
        services.TryAddSingleton<IdentityOidcPrincipalFactory>();
        services.TryAddScoped<IdentityOidcAccessSessionValidator>();
        services.AddOptions<IdentityOidcOptions>()
            .Bind(configuration.GetSection(IdentityOidcOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<IdentityOidcOptions>,
            IdentityOidcOptionsValidator>());
        services.AddIdentityOidcRetention(configuration);

        var oidcOptions = configuration
            .GetSection(IdentityOidcOptions.SectionName)
            .Get<IdentityOidcOptions>() ?? new IdentityOidcOptions();
        if (!oidcOptions.Enable)
        {
            return services;
        }

        services.TryAddSingleton<IdentityOidcSigningKeyRing>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<OpenIddictServerOptions>,
            IdentityOidcServerOptionsConfigurer>());
        services.TryAddScoped<IdentityOidcStoreSqlResolver>();
        services.TryAddScoped<IdentityOidcApplicationStore>();
        services.TryAddScoped<IdentityOidcAuthorizationStore>();
        services.TryAddScoped<IdentityOidcScopeStore>();
        services.TryAddScoped<IdentityOidcTokenStore>();
        services.TryAddScoped<IdentityOidcCenterLoginService>();
        services.TryAddScoped<IdentityOidcAuthorizationService>();
        services.TryAddScoped<IdentityOidcContextAccessTokenIssuer>();
        services.TryAddScoped<IdentityOidcGrantRevocationService>();
        services.Replace(ServiceDescriptor.Scoped<
            IIdentityOidcUserAuthorityRevoker,
            IdentityOidcUserAuthorityRevoker>());
        services.TryAddScoped<IdentityOidcClientConfigResolver>();
        services.TryAddScoped<OidcClientQueryService>();
        services.TryAddScoped<OidcClientManagementService>();
        services.TryAddScoped<OidcAuthorizationQueryService>();
        services.TryAddScoped<OidcAuthorizationManagementService>();
        services.TryAddScoped<OidcSigningKeyQueryService>();
        services.TryAddScoped<OidcSigningKeyManagementService>();
        services.TryAddSingleton<IdentityOidcOpenIddictServerOptionsReloadTokenSource>();
        services.TryAddSingleton<IOptionsChangeTokenSource<OpenIddictServerOptions>>(provider =>
            provider.GetRequiredService<IdentityOidcOpenIddictServerOptionsReloadTokenSource>());
        services.TryAddSingleton<IdentityOidcOpenIddictSigningCredentialSynchronizer>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, IdentityOidcClientRegistrar>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, IdentityOidcProtocolExceptionHandler>());

        services.AddAntiforgery();
        services.AddAuthentication()
            .AddCookie(
                IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme,
                options =>
                {
                    options.Cookie.Name = IdentityOidcCenterAuthenticationDefaults.CookieName;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.LoginPath = "/connect/authorize";
                    options.SlidingExpiration = true;
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                });

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.SetDefaultApplicationEntity<IdentityOidcApplication>();
                options.SetDefaultAuthorizationEntity<IdentityOidcAuthorization>();
                options.SetDefaultScopeEntity<IdentityOidcScope>();
                options.SetDefaultTokenEntity<IdentityOidcToken>();

                options.ReplaceApplicationStore<IdentityOidcApplication, IdentityOidcApplicationStore>();
                options.ReplaceAuthorizationStore<IdentityOidcAuthorization, IdentityOidcAuthorizationStore>();
                options.ReplaceScopeStore<IdentityOidcScope, IdentityOidcScopeStore>();
                options.ReplaceTokenStore<IdentityOidcToken, IdentityOidcTokenStore>();
            })
            .AddServer(options =>
            {
                options.SetIssuer(new Uri(oidcOptions.Issuer, UriKind.Absolute));
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetUserInfoEndpointUris("/connect/userinfo")
                    .SetConfigurationEndpointUris("/.well-known/openid-configuration")
                    .SetJsonWebKeySetEndpointUris("/.well-known/jwks");
                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess);
                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange();
                // 治理测试 V09 要求同一 refresh_token 二次兑换立即失败；默认 30 秒复用宽限期会掩盖重放。
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                options.DisableAccessTokenEncryption();
                // OpenIddict 仍要求注册加密密钥；多实例必须配置共享 EncryptionKeyBase64，否则 refresh 无法跨节点解密。
                if (TryCreateEncryptionKey(oidcOptions.EncryptionKeyBase64, out var encryptionKey))
                {
                    options.AddEncryptionKey(encryptionKey);
                }
                else
                {
                    options.AddEphemeralEncryptionKey();
                }
                options.AddEventHandler(IdentityOidcSignInHandler.Descriptor);
                options.AddEventHandler(IdentityOidcProtocolSessionAuthorityHandler.Descriptor);
                options.AddEventHandler(IdentityOidcRefreshTokenReuseHandler.Descriptor);
                options.UseAspNetCore(aspNetCore =>
                {
                    aspNetCore.EnableAuthorizationEndpointPassthrough();
                    aspNetCore.EnableTokenEndpointPassthrough();
                    aspNetCore.EnableUserInfoEndpointPassthrough();
                    aspNetCore.EnableStatusCodePagesIntegration();
                    aspNetCore.DisableTransportSecurityRequirement();
                });
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IdentityOidcSigningKeyRing, IOptions<IdentityOidcOptions>, Security.RsaSigningKeyRing, IOptions<IdentityOptions>>(
                (jwt, oidcKeyRing, oidcOptionsAccessor, identityKeyRing, identityOptionsAccessor) =>
                {
                    if (!oidcOptionsAccessor.Value.Enable)
                    {
                        return;
                    }

                    var identitySettings = identityOptionsAccessor.Value;
                    var validationKeys = identityKeyRing.ValidationKeys.ToList();
                    foreach (var key in oidcKeyRing.ValidationKeys)
                    {
                        if (!validationKeys.Any(existing =>
                                string.Equals(existing.KeyId, key.KeyId, StringComparison.Ordinal)))
                        {
                            validationKeys.Add(key);
                        }
                    }

                    jwt.TokenValidationParameters.IssuerSigningKeys = validationKeys;
                    jwt.TokenValidationParameters.ValidIssuers =
                    [
                        identitySettings.Issuer,
                        oidcOptionsAccessor.Value.Issuer,
                    ];
                });

        return services;
    }

    private static bool TryCreateEncryptionKey(
        string? encryptionKeyBase64,
        out SymmetricSecurityKey encryptionKey)
    {
        encryptionKey = null!;
        if (string.IsNullOrWhiteSpace(encryptionKeyBase64))
        {
            return false;
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(encryptionKeyBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (keyBytes.Length != 32)
        {
            return false;
        }

        encryptionKey = new SymmetricSecurityKey(keyBytes);
        return true;
    }
}

/// <summary>OIDC 协议注册幂等标记；重复注册会在解析 OpenIddict 选项时失败。</summary>
internal sealed class IdentityOidcRegistrationMarker;
