// Identity 授权相关 DI 扩展；策略、Handler 与权限目录组合。
using System.Diagnostics.CodeAnalysis;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.DataScope;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Features.GetAuthorizationTree;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Identity.DependencyInjection;

/// <summary>Identity 授权相关 DI 扩展；策略、Handler 与权限目录组合。</summary>
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
        services.TryAddSingleton<IIdentityPermissionEvaluator>(provider => provider.GetRequiredService<PermissionClaimEvaluator>());
        services.TryAddScoped<IPermissionSnapshotReader, PermissionSnapshotReader>();
        services.TryAddScoped<IdentityTenantResourceAccess>();
        services.TryAddScoped<IIdentityTenantContextAccess, IdentityTenantContextAccessReader>();
        services.TryAddScoped<
            IIdentitySessionContextService,
            IdentitySessionContextService>();
        services.TryAddSingleton<NavigationProjector>();
        services.TryAddSingleton<AuthorizationTreeProjector>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IAuthorizationHandler,
            FullNetPermissionHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService,
            AuthorizationCatalogValidator>());
        services.AddIdentityDataScope();
        services.AddAuthorization();
        ReplaceAuthorizationServiceIfNeeded<
            IAuthorizationPolicyProvider,
            FullNetPermissionPolicyProvider>(services);
        ReplaceAuthorizationServiceIfNeeded<
            IAuthorizationMiddlewareResultHandler,
            FullNetAuthorizationResultHandler>(services);

        return services;
    }

    /// <summary>API 与后台查询共用权威数据范围，不为 Worker 引入 HTTP 授权中间件。</summary>
    internal static IServiceCollection AddIdentityDataScope(this IServiceCollection services)
    {
        services.TryAddScoped<IUserDataScopeResolver, UserDataScopeResolver>();
        services.TryAddSingleton<RoleDataScopeProjection>();
        services.TryAddSingleton<IDataScopeSqlFilterBuilder, DataScopeSqlFilterBuilder>();
        return services;
    }

    private static void ReplaceAuthorizationServiceIfNeeded<
        TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
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
