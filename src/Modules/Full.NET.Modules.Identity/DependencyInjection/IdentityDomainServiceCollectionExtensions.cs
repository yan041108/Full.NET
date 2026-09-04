using FluentValidation;
using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Features.ManageHostApiKeys;
using Full.NET.Modules.Identity.Features.QueryHostModuleCatalog;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.Features.ManageHostRoleFieldGrants;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Features.ManageSuperAdministrators;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Resources;
using Full.NET.Validation.FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LoginHandler = Full.NET.Modules.Identity.Features.Login.Handler;

namespace Full.NET.Modules.Identity.DependencyInjection;

internal static class IdentityDomainServiceCollectionExtensions
{
    internal static IServiceCollection AddIdentityDomainServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddFullNetLocalization();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            IdentityErrorResourceSource>());
        services.TryAddScoped<ISuperAdministratorService, SuperAdministratorService>();
        services.TryAddScoped<SuperAdministratorManagementService>();
        services.TryAddScoped<SuperAdministratorQueryService>();
        services.TryAddScoped<HostUserQueryService>();
        services.TryAddScoped<HostUserManagementService>();
        services.TryAddScoped<HostUserRolesService>();
        services.TryAddScoped<HostRoleQueryService>();
        services.TryAddScoped<HostRoleManagementService>();
        services.TryAddScoped<HostRoleDataScopeService>();
        services.TryAddSingleton(_ => FieldProjectionCatalog.CreateDefault());
        services.TryAddScoped<IUserFieldProjectionResolver, UserFieldProjectionResolver>();
        services.TryAddScoped<HostRoleFieldGrantService>();
        services.TryAddScoped<HostMenuQueryService>();
        services.TryAddScoped<HostMenuPermissionOptionsQueryService>();
        services.TryAddScoped<HostNavigationCatalogSyncService>();
        services.TryAddScoped<HostMenuManagementService>();
        services.TryAddScoped<HostOnlineSessionQueryService>();
        services.TryAddScoped<HostOnlineSessionManagementService>();
        services.TryAddScoped<HostApiKeyQueryService>();
        services.TryAddScoped<HostApiKeyManagementService>();
        services.TryAddScoped<HostModuleCatalogQueryService>();
        services.TryAddScoped<
            Features.GetHostDashboardSummary.HostDashboardQueryService>();
        services.AddHostUserDirectory();
        services.TryAddScoped<IHostUserDisplayDirectory>(provider =>
            provider.GetRequiredService<HostUsers.HostUserDirectory>());
        services.TryAddScoped<HostUsers.HostUserSelectionDirectory>();
        services.TryAddScoped<IHostUserSelectionDirectory>(provider =>
            provider.GetRequiredService<HostUsers.HostUserSelectionDirectory>());
        services.TryAddScoped<IHostUserBatchSelectionDirectory>(provider =>
            provider.GetRequiredService<HostUsers.HostUserSelectionDirectory>());
        services.TryAddScoped<
            ITenantUserSelectionDirectory,
            HostUsers.TenantUserSelectionDirectory>();
        services.TryAddScoped<HostNavigationDefinitionLoader>();
        services.AddFullNetFluentValidation<Command, LoginSessionResult>();
        services.AddFullNetFluentValidation<
            Features.UpdateLocale.Command,
            LocalePreferenceResponse>();
        services.TryAddScoped<IValidator<Command>, LoginCommandValidator>();
        services.TryAddScoped<
            IValidator<Features.UpdateLocale.Command>,
            Features.UpdateLocale.Validator>();
        services.TryAddScoped<
            ICommandHandler<Command, LoginSessionResult>,
            LoginHandler>();
        services.TryAddScoped<IdentityCookieWriter>();
        services.TryAddScoped<
            ICommandHandler<
                Features.RefreshSession.Command,
                Features.RefreshSession.RefreshSessionResult>,
            Features.RefreshSession.Handler>();
        services.TryAddScoped<
            ICommandHandler<
                Features.Logout.Command,
                Features.Logout.LogoutResult>,
            Features.Logout.Handler>();
        services.TryAddScoped<
            ICommandHandler<
                Features.UpdateLocale.Command,
                LocalePreferenceResponse>,
            Features.UpdateLocale.Handler>();

        return services;
    }

    /// <summary>注册后台消息消费者解析受信用户所需的最小 Host 用户目录。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    /// <returns>原服务集合，便于继续链式注册。</returns>
    internal static IServiceCollection AddHostUserDirectory(this IServiceCollection services)
    {
        services.TryAddScoped<HostUsers.HostUserDirectory>();
        services.TryAddScoped<IHostUserDirectory>(provider =>
            provider.GetRequiredService<HostUsers.HostUserDirectory>());
        return services;
    }
}
