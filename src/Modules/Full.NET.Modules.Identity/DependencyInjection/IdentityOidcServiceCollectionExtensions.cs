// Identity OIDC 服务器 DI 扩展；OpenIddict 存储与端点管线注册。
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
        // 关闭协议入口后仍须允许管理端撤销历史授权，不依赖签发密钥或 OpenIddict 服务。
        services.TryAddScoped<IdentityOidcGrantRevocationService>();
        services.TryAddSingleton<IdentityOidcPrincipalFactory>();
        services.TryAddScoped<IdentityOidcAccessSessionValidator>();
        services.AddOptions<IdentityOidcOptions>()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<IdentityOidcOptions>,
            IdentityOidcOptionsValidator>());
        services.AddIdentityOidcRetention(configuration);

        var oidcOptions = configuration
            .GetSection(IdentityOidcOptions.SectionName)
            .Get<IdentityOidcOptions>() ?? new IdentityOidcOptions();
        var identitySettings = configuration.GetSection(IdentityOptions.SectionName).Get<IdentityOptions>() ?? new();
        if (!oidcOptions.Enable)
        {
            return services;
        }
        if (string.Equals(identitySettings.Issuer, oidcOptions.Issuer, StringComparison.Ordinal))
            throw new InvalidOperationException("Legacy Identity and OIDC token issuers must be distinct.");

        services.TryAddSingleton<IdentityOidcSigningKeyRing>();
        services.TryAddSingleton<IdentityOidcProtocolRequestLimiter>();
        services.TryAddSingleton(new IdentityOidcSigningNodeIdentity("api"));
        services.TryAddScoped<IdentityOidcKeyGovernance>();
        services.AddHealthChecks().AddCheck<IdentityOidcSigningReadiness>("identity-oidc-signing", tags: ["ready"]);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<OpenIddictServerOptions>,
            IdentityOidcServerOptionsConfigurer>());
        services.TryAddScoped<IdentityOidcStoreSqlResolver>();
        services.TryAddScoped<IdentityOidcApplicationStore>();
        services.TryAddScoped<IdentityOidcAuthorizationStore>();
        services.TryAddScoped<IdentityOidcScopeStore>();
        services.TryAddScoped<IdentityOidcTokenStore>();
        services.TryAddScoped<IdentityOidcCenterLoginService>();
        services.TryAddScoped<IdentityOidcMfaVerifier>();
        services.TryAddScoped<IdentityOidcConsentService>();
        services.TryAddScoped<IdentityOidcAccountService>();
        services.TryAddScoped<OidcAuthenticationEventWriter>();
        services.TryAddScoped<IdentityOidcAuthorizationService>();
        services.TryAddScoped<IdentityOidcContextAccessTokenIssuer>();
        services.TryAddScoped<IdentityOidcContextRefreshTokenIssuer>();
        services.Replace(ServiceDescriptor.Scoped<
            IIdentityOidcUserAuthorityRevoker,
            IdentityOidcUserAuthorityRevoker>());
        services.TryAddScoped<IdentityOidcClientConfigResolver>();
        services.TryAddScoped<OidcManagementAuditWriter>();
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
        services.TryAddScoped<IdentityOidcSigningPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, IdentityOidcProtocolExceptionHandler>());

        // 测试环境允许回环明文；生产始终保留 OpenIddict 传输安全校验。
        services.AddOptions<OpenIddict.Server.AspNetCore.OpenIddictServerAspNetCoreOptions>()
            .Configure<IHostEnvironment>((options, environment) =>
                options.DisableTransportSecurityRequirement = environment.IsDevelopment() || environment.IsEnvironment("Testing"));
        services.AddAntiforgery();
        services.AddAuthentication()
            .AddCookie(
                IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme,
                options =>
                {
                    options.Cookie.Name = IdentityOidcCenterAuthenticationDefaults.CookieName;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.LoginPath = "/connect/authorize";
                    options.SlidingExpiration = true;
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                });

        services.AddOpenIddict()
            .AddServer(options =>
            {
                var issuer = new Uri(oidcOptions.Issuer, UriKind.Absolute);
                options.SetIssuer(issuer);
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetUserInfoEndpointUris("/connect/userinfo")
                    .SetEndSessionEndpointUris("/connect/logout")
                    // issuer 含路径时，标准客户端从该路径派生 Discovery；根入口仍支持显式 Metadata 地址。
                    .SetConfigurationEndpointUris(new[] { issuer.AbsolutePath.TrimEnd('/') + "/.well-known/openid-configuration",
                        "/.well-known/openid-configuration" }.Distinct(StringComparer.Ordinal).ToArray())
                    .SetJsonWebKeySetEndpointUris("/.well-known/jwks");
                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess);
                var mcpResource = configuration["FullNet:Ai:Mcp:ResourceIdentifier"];
                if (Uri.TryCreate(mcpResource, UriKind.Absolute, out _)) options.RegisterResources(mcpResource!);
                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange();
                // 协议令牌与权威应用会话采用相同的显式生命周期，避免 JWT 尚有效但会话已到期。
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(identitySettings.AccessTokenMinutes));
                options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(identitySettings.AccessTokenMinutes));
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(identitySettings.RefreshTokenDays));
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
                options.AddEventHandler(IdentityOidcClientScopeHandler.Descriptor);
                options.AddEventHandler(IdentityOidcEndSessionHintHandler.Descriptor);
                options.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(builder =>
                    builder.UseInlineHandler(context =>
                    {
                        context.Metadata["backchannel_logout_supported"] = true;
                        context.Metadata["backchannel_logout_session_supported"] = true;
                        return default;
                    }));
                options.AddEventHandler(IdentityOidcProtocolSessionAuthorityHandler.Descriptor);
                options.AddEventHandler(IdentityOidcRefreshTokenReuseHandler.Descriptor);
                options.AddEventHandler(IdentityOidcProtocolRejectionAuditHandler.Descriptor);
                options.AddEventHandler(IdentityOidcProtocolRateLimitHandler.Descriptor);
                options.UseAspNetCore(aspNetCore =>
                {
                    aspNetCore.EnableAuthorizationEndpointPassthrough();
                    aspNetCore.EnableTokenEndpointPassthrough();
                    aspNetCore.EnableUserInfoEndpointPassthrough();
                    aspNetCore.EnableEndSessionEndpointPassthrough();
                    // OIDC 错误由 OpenIddict 写出标准 OAuth 响应，不能被通用状态页替换。
                });
            });

        services.AddOptions<JwtBearerOptions>(Full.NET.Modules.Identity.Contracts.McpResourceAuthentication.Scheme)
            .Configure<IdentityOidcSigningKeyRing, IOptions<IdentityOidcOptions>>((jwt, keyRing, options) =>
            {
                var resource = configuration["FullNet:Ai:Mcp:ResourceIdentifier"] ?? "fullnet://ai/mcp";
                jwt.MapInboundClaims = false;
                jwt.EventsType = typeof(Security.FullNetJwtBearerEvents);
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = (_, token, kid, _) => token.Issuer == options.Value.Issuer
                        ? (string.IsNullOrEmpty(kid) ? keyRing.ValidationKeys : keyRing.ValidationKeys.Where(key => key.KeyId == kid))
                        : [],
                    TryAllIssuerSigningKeys = false,
                    ValidateIssuer = true,
                    ValidIssuer = options.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = resource,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Name,
                };
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IdentityOidcSigningKeyRing, IOptions<IdentityOidcOptions>, Security.RsaSigningKeyRing, IOptions<IdentityOptions>>(
                (jwt, oidcKeyRing, oidcOptionsAccessor, identityKeyRing, identityOptionsAccessor) =>
                {
                    if (!oidcOptionsAccessor.Value.Enable)
                    {
                        return;
                    }

                    var identityTrust = identityOptionsAccessor.Value;
                    // 按 issuer 隔离信任集，并逐次读取活动验签集合；退役不依赖 JwtBearer Options 重建。
                    jwt.TokenValidationParameters.IssuerSigningKey = null;
                    jwt.TokenValidationParameters.IssuerSigningKeys = [];
                    jwt.TokenValidationParameters.TryAllIssuerSigningKeys = false;
                    jwt.TokenValidationParameters.IssuerSigningKeyResolver = (_, token, kid, _) =>
                    {
                        IEnumerable<SecurityKey> source = token.Issuer == oidcOptionsAccessor.Value.Issuer ? oidcKeyRing.ValidationKeys
                            : token.Issuer == identityTrust.Issuer ? identityKeyRing.ValidationKeys : [];
                        return string.IsNullOrEmpty(kid) ? source : source.Where(key => key.KeyId == kid);
                    };
                    jwt.TokenValidationParameters.ValidIssuers =
                    [
                        identityTrust.Issuer,
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
