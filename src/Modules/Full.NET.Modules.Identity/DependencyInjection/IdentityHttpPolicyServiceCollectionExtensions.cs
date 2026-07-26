using Full.NET.Hosting.RateLimiting;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.RateLimiting;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Serialization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.DependencyInjection;

internal static class IdentityHttpPolicyServiceCollectionExtensions
{
    internal static IServiceCollection AddIdentityHttpPolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<AllowedOriginValidator>();
        services.AddCors();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<CorsOptions>,
            IdentityCorsOptionsConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<RateLimiterOptions>,
            IdentityRateLimiterPolicyConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<RateLimitPolicyErrorCodes>,
            IdentityRateLimiterPolicyConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<JsonOptions>,
            IdentityHttpJsonOptionsConfigurator>());

        return services;
    }
}

internal sealed class IdentityCorsOptionsConfigurator(
    IOptions<IdentityOptions> identityOptions)
    : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions options)
    {
        var allowedOrigins = identityOptions.Value.AllowedOrigins
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var policy = new CorsPolicyBuilder();
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }

        options.AddPolicy(IdentityModule.BrowserCorsPolicy, policy.Build());
    }
}

internal sealed class IdentityHttpJsonOptionsConfigurator
    : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        if (!options.SerializerOptions.TypeInfoResolverChain.Contains(
                IdentityJsonSerializerContext.Default))
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                IdentityJsonSerializerContext.Default);
        }
    }
}
