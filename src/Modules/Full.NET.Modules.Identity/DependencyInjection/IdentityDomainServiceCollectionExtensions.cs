using FluentValidation;
using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Features.ManageHostApiKeys;
using Full.NET.Modules.Identity.Features.ManageOpenAccessClients;
using Full.NET.Modules.Identity.Features.ManageLdapConnections;
using Full.NET.Modules.Identity.Features.ManageOAuthProviders;
using Full.NET.Modules.Identity.Features.ManageOAuthLinks;
using Full.NET.Modules.Identity.Features.OAuthFlow;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Features.ManageRegistrationWays;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.AcceptTenantInvitation;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.PublicRegistrationWays;
using Full.NET.Modules.Identity.Features.RecoverAccount;
using Full.NET.Modules.Identity.Features.RegisterAccount;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Identity.Features.QueryHostModuleCatalog;
using Full.NET.Modules.Identity.Features.QueryHostModuleSelection;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.Features.ManageHostRoleFieldGrants;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Features.SelfServiceProfile;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Features.ManageSuperAdministrators;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Resources;
using Full.NET.Validation.FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LoginHandler = Full.NET.Modules.Identity.Features.Login.Handler;
using LoginCommand = Full.NET.Modules.Identity.Features.Login.Command;

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
        services.TryAddScoped<Security.AuthenticationSecurityEventWriter>();
        services.TryAddScoped<IIdentityOidcUserAuthorityRevoker, NullIdentityOidcUserAuthorityRevoker>();
        services.TryAddScoped<HostUserSensitiveFieldRevealService>();
        services.TryAddScoped<HostUserLoginLockoutUnlockService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileRetentionContributor,
            Features.HostFileReferences.IdentityHostFileRetentionContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostFileReferenceClaimProbe,
            Features.HostFileReferences.IdentityUserProfileMediaReferenceProbe>());
        services.TryAddScoped<SelfServiceProfileService>();
        services.TryAddScoped<SelfServiceProfileMediaService>();
        services.TryAddScoped<HostUserRolesService>();
        services.TryAddScoped<HostRoleQueryService>();
        services.TryAddScoped<HostRoleManagementService>();
        services.TryAddScoped<HostRoleMembersService>();
        services.TryAddScoped<HostRoleDataScopeService>();
        services.TryAddSingleton(_ => FieldProjectionCatalog.CreateDefault());
        services.TryAddScoped<IUserFieldProjectionResolver, UserFieldProjectionResolver>();
        services.TryAddScoped<HostRoleFieldGrantService>();
        services.TryAddScoped<HostMenuQueryService>();
        services.TryAddScoped<HostMenuPermissionOptionsQueryService>();
        services.TryAddScoped<HostNavigationCatalogSyncService>();
        services.TryAddScoped<HostMenuManagementService>();
        services.TryAddScoped<HostOnlineSessionQueryService>();
        services.TryAddScoped<Features.QueryAuthenticationEvents.AuthenticationEventQueryService>();
        services.TryAddScoped<Features.QueryAuthenticationEvents.AuthenticationEventExportService>();
        services.TryAddScoped<HostOnlineSessionManagementService>();
        services.TryAddScoped<IdentitySessionPolicyQueryService>();
        services.TryAddScoped<IdentitySessionRealtimeDelivery>();
        services.TryAddScoped<HostApiKeyQueryService>();
        services.TryAddScoped<HostApiKeyManagementService>();
        services.TryAddScoped<OpenAccessClientQueryService>();
        services.TryAddScoped<OpenAccessClientManagementService>();
        services.TryAddScoped<OpenAccessClientAccessSupport>();
        services.TryAddScoped<OpenAccessClientObservabilityService>();
        services.TryAddScoped<RegistrationPolicyService>();
        services.TryAddScoped<AccountChallengeService>();
        services.TryAddScoped<RegistrationInvitationService>();
        services.TryAddScoped<IIdentityChallengeDeliveryPort, NullIdentityChallengeDeliveryPort>();
        services.TryAddScoped<ITenantMemberSeatQuotaPort, NullTenantMemberSeatQuotaPort>();
        services.TryAddScoped<RegistrationWayQueryService>();
        services.TryAddScoped<RegistrationWayManagementService>();
        services.TryAddScoped<TenantMembershipQueryService>();
        services.TryAddScoped<TenantMembershipManagementService>();
        services.TryAddScoped<TenantMemberProvisionService>();
        services.TryAddScoped<AcceptTenantInvitationService>();
        services.TryAddScoped<Features.ManageMyTenantInvitations.MyTenantInvitationQueryService>();
        services.TryAddScoped<PublicRegistrationWayQueryService>();
        services.TryAddScoped<LdapConnectionQueryService>();
        services.TryAddScoped<LdapConnectionManagementService>();
        services.TryAddScoped<LdapConnectionOperationsService>();
        services.TryAddScoped<OAuthProviderQueryService>();
        services.TryAddScoped<OAuthProviderManagementService>();
        services.TryAddScoped<OAuthFlowService>();
        services.TryAddScoped<IdentityOAuthLoginSessionService>();
        services.TryAddScoped<OAuthUserLinkQueryService>();
        services.TryAddScoped<OAuthUserLinkManagementService>();
        services.TryAddSingleton<OAuth.IOidcClient, OAuth.HttpOidcClient>();
        services.TryAddSingleton<OAuth.OAuthReturnUrlValidator>();
        services.TryAddScoped<HostModuleCatalogQueryService>();
        services.TryAddScoped<HostModuleSelectionQueryService>();
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
        services.TryAddScoped<
            ITenantMemberSelectionDirectory,
            HostUsers.TenantMemberSelectionDirectory>();
        services.TryAddScoped<HostUsers.HostTenantUserSelectionDirectory>();
        services.TryAddScoped<IHostTenantUserSelectionDirectory>(provider =>
            provider.GetRequiredService<HostUsers.HostTenantUserSelectionDirectory>());
        services.TryAddScoped<IWorkflowRoleMemberDirectory, Workflow.WorkflowRoleMemberDirectory>();
        services.TryAddScoped<HostNavigationDefinitionLoader>();
        services.AddFullNetFluentValidation<LoginCommand, LoginSessionResult>();
        services.AddFullNetFluentValidation<
            Features.UpdateLocale.Command,
            LocalePreferenceResponse>();
        services.AddFullNetFluentValidation<
            Features.ChangePassword.Command,
            Features.ChangePassword.ChangePasswordSessionResult>();
        services.AddFullNetFluentValidation<
            Features.SelfServiceProfile.UpdateCommand,
            SelfServiceProfileResponse>();
        services.TryAddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
        services.TryAddScoped<
            IValidator<Features.UpdateLocale.Command>,
            Features.UpdateLocale.Validator>();
        services.TryAddScoped<
            IValidator<Features.ChangePassword.Command>,
            Features.ChangePassword.Validator>();
        services.TryAddScoped<
            IValidator<UpdateCommand>,
            UpdateValidator>();
        services.TryAddScoped<
            ICommandHandler<LoginCommand, LoginSessionResult>,
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
        services.TryAddScoped<
            ICommandHandler<
                Features.ChangePassword.Command,
                Features.ChangePassword.ChangePasswordSessionResult>,
            Features.ChangePassword.Handler>();
        services.TryAddScoped<
            ICommandHandler<UpdateCommand, SelfServiceProfileResponse>,
            UpdateHandler>();
        services.AddFullNetFluentValidation<
            Features.RegisterAccount.Command,
            RegisterAccountResponse>();
        services.TryAddScoped<
            IValidator<Features.RegisterAccount.Command>,
            Features.RegisterAccount.Validator>();
        services.TryAddScoped<
            ICommandHandler<Features.RegisterAccount.Command, RegisterAccountResponse>,
            Features.RegisterAccount.Handler>();
        services.TryAddScoped<
            ICommandHandler<Features.RecoverAccount.RequestCommand, AccountChallengeAcceptedResponse>,
            Features.RecoverAccount.RequestHandler>();
        services.TryAddScoped<
            ICommandHandler<Features.RecoverAccount.ConfirmCommand, bool>,
            Features.RecoverAccount.ConfirmHandler>();

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
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IHostActiveUserCountReader,
            HostUsers.HostActiveUserCountReader>());
        return services;
    }

    /// <summary>注册 Workflow 通知投影按 Host/Tenant 作用域批量校验收件人所需的最小目录。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    /// <returns>原服务集合，便于继续链式注册。</returns>
    internal static IServiceCollection AddNotificationRecipientDirectories(
        this IServiceCollection services)
    {
        services.AddHostUserDirectory();
        services.TryAddScoped<HostUsers.HostUserSelectionDirectory>();
        services.TryAddScoped<IHostUserBatchSelectionDirectory>(provider =>
            provider.GetRequiredService<HostUsers.HostUserSelectionDirectory>());
        services.TryAddScoped<ITenantUserSelectionDirectory, HostUsers.TenantUserSelectionDirectory>();
        return services;
    }
}
