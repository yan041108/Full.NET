using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Features.ManageHostApiKeys;
using Full.NET.Modules.Identity.Features.ManageTotp;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.DependencyInjection;

internal static class IdentityAuthenticationServiceCollectionExtensions
{
    internal static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 命名 Scheme 的配置委托不具备幂等性，重复注册会在解析 AuthenticationOptions 时失败。
        if (services.Any(descriptor =>
                descriptor.ServiceType
                == typeof(IdentityAuthenticationRegistrationMarker)))
        {
            return services;
        }

        services.TryAddSingleton<IdentityAuthenticationRegistrationMarker>();
        services.TryAddScoped<AccessSessionValidator>();
        services.TryAddScoped<FullNetJwtBearerEvents>();
        // Data Protection 由宿主 AddFullNetDataProtection 统一配置共享 Key Ring，禁止此处裸注册。
        services.TryAddSingleton<TotpSecretProtector>();

        var enableTotpStrongReauthentication = configuration.GetValue(
            $"{IdentityOptions.SectionName}:EnableTotpStrongReauthentication",
            false);
        if (enableTotpStrongReauthentication)
        {
            services.TryAddScoped<
                IStrongReauthenticationProvider,
                TotpStrongReauthenticationProvider>();
        }
        else
        {
            services.TryAddScoped<
                IStrongReauthenticationProvider,
                PasswordReauthenticationProvider>();
        }

        services.TryAddScoped<TotpEnrollmentService>();
        services.TryAddScoped<ApiKeyAuthenticationService>();
        services.TryAddScoped<SignatureAuthenticationService>();
        services.AddOptions<SignatureAuthenticationOptions>()
            .Bind(configuration.GetSection(SignatureAuthenticationOptions.SectionName));
        services.TryAddSingleton<RsaSigningKeyRing>();
        services.TryAddSingleton<IRandomTokenGenerator, CryptographicTokenGenerator>();
        services.TryAddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    SmartAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme =
                    JwtBearerDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme(
                SmartAuthenticationDefaults.AuthenticationScheme,
                "Authorization Bearer or ApiKey",
                options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        var authorization =
                            context.Request.Headers.Authorization.ToString();
                        return authorization.StartsWith(
                                "ApiKey ",
                                StringComparison.OrdinalIgnoreCase)
                            ? ApiKeyAuthenticationDefaults.AuthenticationScheme
                            : JwtBearerDefaults.AuthenticationScheme;
                    };
                })
            .AddJwtBearer(options => options.MapInboundClaims = false)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.AuthenticationScheme,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, SignatureAuthenticationHandler>(
                SignatureAuthenticationDefaults.AuthenticationScheme,
                _ => { });
        services.AddOptions<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme)
            .Configure<RsaSigningKeyRing, IOptions<IdentityOptions>>(
                (jwt, keyRing, identityOptions) =>
                {
                    var settings = identityOptions.Value;
                    jwt.MapInboundClaims = false;
                    jwt.EventsType = typeof(FullNetJwtBearerEvents);
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeys = keyRing.ValidationKeys,
                        ValidateIssuer = true,
                        ValidIssuer = settings.Issuer,
                        ValidateAudience = true,
                        ValidAudience = settings.Audience,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                        NameClaimType = JwtRegisteredClaimNames.Name,
                    };
                });

        return services;
    }
}

internal sealed class IdentityAuthenticationRegistrationMarker;
