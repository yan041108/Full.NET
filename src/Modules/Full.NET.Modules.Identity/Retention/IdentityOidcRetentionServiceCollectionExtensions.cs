using Full.NET.Abstractions.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Retention;

internal static class IdentityOidcRetentionServiceCollectionExtensions
{
    private const string ObservabilityAdminHostRoleKey = "FullNet:ObservabilityAdmin:HostRole";

    internal static IServiceCollection AddIdentityOidcRetention(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<IdentityOidcRetentionOptions>()
            .Bind(configuration.GetSection(IdentityOidcRetentionOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<IdentityOidcRetentionOptions>,
            IdentityOidcRetentionOptionsValidator>());
        services.TryAddScoped<IdentityOidcRetentionRunner>();
        return services;
    }

    internal static IServiceCollection AddIdentityOidcRetentionBackgroundService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityOidcRetention(configuration);
        services.TryAddSingleton<IClock, SystemClock>();

        var hostRole = configuration[ObservabilityAdminHostRoleKey] ?? "Api";
        if (string.Equals(hostRole, "Api", StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        services.AddHostedService<IdentityOidcRetentionHostedProcessor>();
        return services;
    }

    internal static IServiceCollection AddAuthenticationEventRetention(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuthenticationEventRetentionOptions>()
            .Bind(configuration.GetSection(AuthenticationEventRetentionOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AuthenticationEventRetentionOptions>,
            AuthenticationEventRetentionOptionsValidator>());
        services.TryAddScoped<AuthenticationEventRetentionRunner>();
        return services;
    }

    internal static IServiceCollection AddAuthenticationEventRetentionBackgroundService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthenticationEventRetention(configuration);
        services.AddHostedService<AuthenticationEventRetentionHostedProcessor>();

        return services;
    }
}
