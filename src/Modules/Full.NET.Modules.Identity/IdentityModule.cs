using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Catalogs;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.DependencyInjection;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Bootstrap;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Features.OrganizationUnitProjection;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Seeding;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Seeding.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity;

public sealed class IdentityModule : IFullNetModule
{
    /// <summary>
    /// 匿名会话轮换与退出共享的限流策略，避免攻击者绕过登录限流持续消耗密码学与数据库资源。
    /// </summary>
    internal const string SessionMutationRateLimitPolicy = "identity-session-mutation";

    /// <summary>开放接口签名认证限流策略。</summary>
    internal const string SignatureAuthenticationRateLimitPolicy = "identity-signature-auth";

    /// <summary>
    /// 浏览器管理端使用的精确来源 CORS 策略名称。
    /// </summary>
    public const string BrowserCorsPolicy = "FullNET.Identity.BrowserClients";

    public string Name => "Identity";

    public IReadOnlyCollection<string> Dependencies => [];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddMigrationServices(services, configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IEnumCatalogContributor,
            IdentityEnumCatalogContributor>());
        services.AddIdentityAuthentication(configuration);
        services.AddIdentityAuthorization(configuration);
        services.AddIdentityDomainServices(configuration);
        services.AddIdentityHttpPolicies(configuration);
        AddOrganizationUnitProjection(services);
        AddBackgroundServices(services, configuration);
    }

    public void AddMigrationServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<IdentityOptions>,
            IdentityOptionsValidator>());
        services.AddOptions<SignatureAuthenticationOptions>()
            .Bind(configuration.GetSection(SignatureAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<SignatureAuthenticationOptions>,
            SignatureAuthenticationOptionsValidator>());
        services.TryAddSingleton<IClock, Abstractions.Time.SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<
            Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser>,
            Microsoft.AspNetCore.Identity.PasswordHasher<IdentityUser>>();
        services.TryAddScoped<IIdentityBootstrapService, IdentityBootstrapService>();
        AddHostNavigationCatalogSeedClosure(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDataSeedContributor,
            HostAdministratorSeedContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IDataSeedContributor,
            HostNavigationCatalogSeedContributor>());
    }

    /// <summary>
    /// 为 Migrator Seed 注册导航目录同步所需的最小闭包，避免拉起完整 HTTP/认证运行时。
    /// </summary>
    private static void AddHostNavigationCatalogSeedClosure(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            IdentityAuthorizationContributor>());
        services.TryAddSingleton(provider => AuthorizationCatalog.Create(
            provider.GetServices<IAuthorizationCatalogContributor>()));
        services.TryAddScoped<HostNavigationCatalogSyncService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Identity");
        Features.Login.Endpoint.Map(group);
        Features.RefreshSession.Endpoint.Map(group);
        Features.Logout.Endpoint.Map(group);
        Features.GetCurrentUser.Endpoint.Map(endpoints);
        Features.UpdateLocale.Endpoint.Map(endpoints);
        Features.GetNavigation.Endpoint.Map(endpoints);
        Features.GetAuthorizationTree.Endpoint.Map(endpoints);
        Features.ManageSuperAdministrators.Endpoint.Map(endpoints);
        Features.ManageTotp.Endpoint.Map(endpoints);
        Features.ManageHostUsers.Endpoint.Map(endpoints);
        Features.ManageHostRoles.Endpoint.Map(endpoints);
        Features.ManageHostRoleFieldGrants.Endpoint.Map(endpoints);
        Features.ManageHostMenus.Endpoint.Map(endpoints);
        Features.ManageHostOnlineSessions.Endpoint.Map(endpoints);
        Features.ManageHostApiKeys.Endpoint.Map(endpoints);
        Features.QueryHostModuleCatalog.Endpoint.Map(endpoints);
        Features.GetHostDashboardSummary.Endpoint.Map(endpoints);
        Features.OrganizationUnitProjection.Endpoint.Map(endpoints);
    }

    /// <summary>注册 Worker 消费机构单元投影事件所需的最小后台能力。</summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddOrganizationUnitProjection(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            OrganizationUnitChangedIntegrationEventHandler>());
    }

    private static void AddOrganizationUnitProjection(IServiceCollection services)
    {
        services.TryAddScoped<OrganizationUnitProjectionWriter>();
        services.TryAddScoped<OrganizationUnitProjectionDirectory>();
        services.TryAddScoped<IOrganizationUnitProjectionDirectory>(provider =>
            provider.GetRequiredService<OrganizationUnitProjectionDirectory>());
        services.TryAddScoped<OrganizationUnitProjectionBackfillService>();
        services.TryAddScoped<OrganizationUnitProjectionReconciliationService>();
    }
}
