using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc.Stores;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        services.AddOptions<IdentityOidcOptions>()
            .Bind(configuration.GetSection(IdentityOidcOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<IdentityOidcOptions>,
            IdentityOidcOptionsValidator>());

        var oidcOptions = configuration
            .GetSection(IdentityOidcOptions.SectionName)
            .Get<IdentityOidcOptions>() ?? new IdentityOidcOptions();
        if (!oidcOptions.Enable)
        {
            return services;
        }

        services.TryAddScoped<IdentityOidcStoreSqlResolver>();
        services.TryAddScoped<IdentityOidcApplicationStore>();
        services.TryAddScoped<IdentityOidcAuthorizationStore>();
        services.TryAddScoped<IdentityOidcScopeStore>();
        services.TryAddScoped<IdentityOidcTokenStore>();

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
                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();
                options.UseAspNetCore();
            });

        return services;
    }
}

/// <summary>OIDC 协议注册幂等标记；重复注册会在解析 OpenIddict 选项时失败。</summary>
internal sealed class IdentityOidcRegistrationMarker;