using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.DataScope;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Identity.DependencyInjection;

internal static class IdentityAuthorizationServiceCollectionExtensions
{
    internal static IServiceCollection AddIdentityAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            IdentityAuthorizationContributor>());
        services.TryAddSingleton(provider => AuthorizationCatalog.Create(
            provider.GetServices<IAuthorizationCatalogContributor>()));
        services.TryAddSingleton<PermissionClaimEvaluator>();
        services.TryAddScoped<IPermissionSnapshotReader, PermissionSnapshotReader>();
        services.TryAddScoped<
            IIdentitySessionContextService,
            IdentitySessionContextService>();
        services.TryAddSingleton<NavigationProjector>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationHandler,
            FullNetPermissionHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService,
            AuthorizationCatalogValidator>());
        services.TryAddScoped<IUserDataScopeResolver, UserDataScopeResolver>();
        services.TryAddSingleton<RoleDataScopeProjection>();
        services.TryAddSingleton<
            IDataScopeSqlFilterBuilder,
            DataScopeSqlFilterBuilder>();
        services.AddAuthorization();
        ReplaceAuthorizationServiceIfNeeded<
            IAuthorizationPolicyProvider,
            FullNetPermissionPolicyProvider>(services);
        ReplaceAuthorizationServiceIfNeeded<
            IAuthorizationMiddlewareResultHandler,
            FullNetAuthorizationResultHandler>(services);

        return services;
    }

    private static void ReplaceAuthorizationServiceIfNeeded<
        TService,
        TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var effectiveDescriptor = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(TService));
        if (effectiveDescriptor?.ImplementationType == typeof(TImplementation))
        {
            return;
        }

        services.Replace(ServiceDescriptor.Singleton<
            TService,
            TImplementation>());
    }
}
