using System.Text.RegularExpressions;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Hosting.Observability;
using Full.NET.Modularity.Modules;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MemoryPack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 锁定客户端生成已批准资源组的 Operation 身份，并阻止显式 Endpoint 名称发生冲突。
/// </summary>
[TestClass]
public sealed partial class OpenApiOperationIdentityRulesTests
{
    private const string IdentityHostUsersTag = "IdentityHostUsers";
    private const string IdentityHostRolesTag = "IdentityHostRoles";
    private const string IdentityHostMenusTag = "IdentityHostMenus";
    private const string IdentityHostApiKeysTag = "IdentityHostApiKeys";
    private const string IdentityOpenAccessClientsTag = "IdentityOpenAccessClients";
    private const string IdentityRegistrationPolicyTag = "IdentityRegistrationPolicy";
    private const string IdentityRegistrationWaysTag = "IdentityRegistrationWays";
    private const string IdentityLdapConnectionsTag = "IdentityLdapConnections";
    private const string IdentityOAuthProvidersTag = "IdentityOAuthProviders";
    private const string IdentityOAuthPublicTag = "IdentityOAuthPublic";
    private const string IdentityOAuthLinksTag = "IdentityOAuthLinks";
    private const string IdentityPublicRegistrationTag = "IdentityPublicRegistration";
    private const string IdentityHostOnlineSessionsTag = "IdentityHostOnlineSessions";
    private const string IdentityHostModulesTag = "IdentityHostModules";
    private const string IdentityMeTag = "IdentityMe";
    private const string IdentityTotpEnrollmentTag = "IdentityTotpEnrollment";
    private const string IdentitySuperAdministratorsTag = "IdentitySuperAdministrators";
    private const string IdentityAuthSessionTag = "IdentityAuthSession";
    private const string TenancyHostTenantsTag = "TenancyHostTenants";
    private const string TenancyHostTenantPackagesTag = "TenancyHostTenantPackages";
    private const string OrganizationTenantUnitsTag = "OrganizationTenantUnits";
    private const string OrganizationTenantUserUnitsTag = "OrganizationTenantUserUnits";
    private const string OrganizationTenantPositionsTag = "OrganizationTenantPositions";
    private const string OrganizationTenantPositionLevelsTag = "OrganizationTenantPositionLevels";
    private const string OrganizationTenantUserPositionsTag = "OrganizationTenantUserPositions";
    private const string OrganizationHostUserManagementTag = "OrganizationHostUserManagement";
    private const string FilesTag = "FilesHostFiles";
    private const string FilesFoldersTag = "FilesHostFolders";
    private const string FilesStorageProvidersTag = "FilesStorageProviders";
    private const string SettingsTag = "SettingsHostConfigEntries";
    private const string SettingsDiagnosticPolicyTag = "SettingsHostDiagnosticPolicy";
    private const string SettingsHostDictTypesTag = "SettingsHostDictTypes";
    private const string SettingsHostEnumCatalogsTag = "SettingsHostEnumCatalogs";
    private const string SettingsTenantDictTypesTag = "SettingsTenantDictTypes";
    private const string AuditingHostAccessLogsTag = "AuditingHostAccessLogs";
    private const string AuditingHostOperationLogsTag = "AuditingHostOperationLogs";
    private const string AuditingHostExceptionLogsTag = "AuditingHostExceptionLogs";
    private const string AuditingHostOutboundCallLogsTag = "AuditingHostOutboundCallLogs";
    private const string PlatformHostDashboardTag = "PlatformHostDashboard";
    private const string JobsHostJobDefinitionsTag = "JobsHostJobDefinitions";
    private const string JobsHostJobExecutionsTag = "JobsHostJobExecutions";
    private const string JobsHostJobSchedulesTag = "JobsHostJobSchedules";
    private const string JobsHostJobHealthTag = "JobsHostJobHealth";
    private const string CalendarMyPersonalSchedulesTag = "CalendarMyPersonalSchedules";
    private const string PlatformHostReleaseNotesTag = "PlatformHostReleaseNotes";
    private const string PlatformMyReleaseNotesTag = "PlatformMyReleaseNotes";
    private const string PlatformBackupExecutorTag = "PlatformBackupExecutor";
    private const string RegionsAdministrativeRegionsTag = "RegionsAdministrativeRegions";
    private const string NotificationsHostAnnouncementsTag = "NotificationsHostAnnouncements";
    private const string NotificationsMyHostAnnouncementsTag = "NotificationsMyHostAnnouncements";
    private const string NotificationsMyInboxMessagesTag = "NotificationsMyInboxMessages";
    private const string NotificationsHostInboxMessagesTag = "NotificationsHostInboxMessages";
    private const string CodeGenerationPreviewsTag = "CodeGenerationPreviews";
    private const string CodeGenerationRunsTag = "CodeGenerationRuns";
    private const string CodeGenerationTemplatesTag = "CodeGenerationTemplates";
    private const string CodeGenerationCatalogTag = "CodeGenerationCatalog";
    private const string SerialNumbersHostRulesTag = "SerialNumbersHostRules";
    private const string ObservabilityLogFilesTag = "ObservabilityLogFiles";
    private const string ObservabilityServerMonitorTag = "ObservabilityServerMonitor";
    private const string ObservabilityCachePoliciesTag = "ObservabilityCachePolicies";
    private const string ObservabilityElasticsearchLogPipelineTag = "ObservabilityElasticsearchLogPipeline";
    private const string MqttControlPlaneTag = "MqttControlPlane";
    private const string CryptographyGmKeysTag = "CryptographyGmKeys";
    private const string DocumentHostCategoriesTag = "DocumentHostCategories";
    private const string DocumentHostItemsTag = "DocumentHostItems";
    private const string DocumentHostTagsTag = "DocumentHostTags";
    private const string DocumentHostPermissionsTag = "DocumentHostPermissions";
    private const string DocumentHostRecycleBinTag = "DocumentHostRecycleBin";
    private const string DocumentHostSharesTag = "DocumentHostShares";
    private const string DocumentPublicSharesTag = "DocumentPublicShares";
    private const string DocumentHostStatisticsTag = "DocumentHostStatistics";
    private const string DataApprovalRequestsTag = "DataApprovalRequests";
    private const string DataApprovalScenariosTag = "DataApprovalScenarios";

    [TestMethod]
    public void Approved_client_generation_operations_have_unique_lower_camel_names_and_one_primary_tag()
    {
        using var app = BuildApiApplication();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var endpointsByKey = endpoints
            .SelectMany(endpoint => ResolveMethods(endpoint)
                .Select(method => new
                {
                    Key = $"{method} {NormalizeRoute(endpoint.RoutePattern.RawText)}",
                    Endpoint = endpoint,
                }))
            .ToDictionary(item => item.Key, item => item.Endpoint, StringComparer.Ordinal);

        foreach (var expected in ExpectedOperations)
        {
            var key = $"{expected.Method} {expected.Route}";
            Assert.IsTrue(endpointsByKey.TryGetValue(key, out var endpoint), $"缺少批准 Endpoint：{key}");
            Assert.AreEqual(
                expected.OperationId,
                endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                $"{key} 的 Operation 名称不稳定。");
            var tags = endpoint.Metadata
                .GetOrderedMetadata<ITagsMetadata>()
                .SelectMany(metadata => metadata.Tags)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                new[] { expected.PrimaryTag },
                tags,
                $"{key} 必须恰有一个批准主 Tag。");
        }

        var explicitNames = endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
        var duplicateNames = explicitNames
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.HasCount(0, duplicateNames, $"发现重复 Operation 名称：{string.Join(", ", duplicateNames)}");

        var invalidNames = explicitNames
            .Where(name => !LowerCamelOperationIdPattern().IsMatch(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.HasCount(0, invalidNames, $"Operation 名称必须为 lowerCamelCase：{string.Join(", ", invalidNames)}");
    }

    private static WebApplication BuildApiApplication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Testing";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = DatabaseProvider.SqlServer.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                "Server=127.0.0.1,1;Database=fullnet_architecture;User Id=sa;Password=FullNet_Test!123;TrustServerCertificate=True;Connect Timeout=1",
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                MySqlGuidStorageMode.Binary16.ToString(),
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
            ["Identity:EnableRemoteSuperAdministratorManagement"] = "true",
            ["Identity:AllowedOrigins:0"] = "http://localhost",
            ["Tenancy:HostDomains:0"] = "localhost",
        });
        builder.AddFullNetServiceDefaults();
        builder.Services.AddFullNetDapper(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFullNetDatabaseSchemaModeGuard();
        builder.Services.AddFullNetMemoryPack();
        builder.Services.AddFullNetCaching(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFullNetRealtimeSignalR(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        builder.Services.AddFullNetApplicationModules(
            builder.Configuration,
            FullNetHostProfile.Api);

        var app = builder.Build();
        app.MapFullNetModules();
        return app;
    }

    private static IReadOnlyList<string> ResolveMethods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
        ?? new[] { HttpMethods.Get };

    private static string NormalizeRoute(string? route) =>
        RouteConstraintPattern().Replace(
            (route ?? string.Empty).TrimEnd('/'),
            "{$1}");

    [GeneratedRegex("^[a-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex LowerCamelOperationIdPattern();

    [GeneratedRegex("\\{([^}:]+):[^}]+\\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteConstraintPattern();

    private static readonly ApprovedClientGenerationOperation[] ExpectedOperations =
    [
        new("GET", "/api/v1/identity/users", "identityListHostUsers", IdentityHostUsersTag),
        new("GET", "/api/v1/identity/users/export", "identityExportHostUsers", IdentityHostUsersTag),
        new("GET", "/api/v1/identity/users/export-file", "identityExportHostUsersWorkbook", IdentityHostUsersTag),
        new("GET", "/api/v1/identity/users/import-template", "identityDownloadHostUserImportTemplate", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/import", "identityImportHostUsers", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/import-file", "identityImportHostUsersWorkbook", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/batch-disable", "identityBatchDisableHostUsers", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/batch-enable", "identityBatchEnableHostUsers", IdentityHostUsersTag),
        new("GET", "/api/v1/identity/users/{userId}", "identityGetHostUser", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users", "identityCreateHostUser", IdentityHostUsersTag),
        new("PUT", "/api/v1/identity/users/{userId}", "identityUpdateHostUser", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/{userId}/disable", "identityDisableHostUser", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/{userId}/enable", "identityEnableHostUser", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/{userId}/reset-password", "identityResetHostUserPassword", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/{userId}/unlock-login", "identityUnlockHostUserLogin", IdentityHostUsersTag),
        new("GET", "/api/v1/identity/users/{userId}/roles", "identityGetHostUserRoles", IdentityHostUsersTag),
        new("PUT", "/api/v1/identity/users/{userId}/roles", "identityReplaceHostUserRoles", IdentityHostUsersTag),
        new("POST", "/api/v1/identity/users/{userId}/reveal-profile-fields", "identityRevealHostUserProfileFields", IdentityHostUsersTag),
        new("GET", "/api/v1/identity/authorization-tree", "identityGetAuthorizationTree", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/field-projections/catalog", "identityListFieldProjectionCatalog", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/roles", "identityListHostRoles", IdentityHostRolesTag),
        new("POST", "/api/v1/identity/roles", "identityCreateHostRole", IdentityHostRolesTag),
        new("POST", "/api/v1/identity/roles/{sourceRoleId}/copy", "identityCopyHostRole", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/roles/{roleId}", "identityGetHostRole", IdentityHostRolesTag),
        new("PUT", "/api/v1/identity/roles/{roleId}", "identityUpdateHostRole", IdentityHostRolesTag),
        new("PUT", "/api/v1/identity/roles/{roleId}/permissions", "identityReplaceHostRolePermissions", IdentityHostRolesTag),
        new("POST", "/api/v1/identity/roles/{roleId}/disable", "identityDisableHostRole", IdentityHostRolesTag),
        new("POST", "/api/v1/identity/roles/{roleId}/enable", "identityEnableHostRole", IdentityHostRolesTag),
        new("DELETE", "/api/v1/identity/roles/{roleId}", "identityDeleteHostRole", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/roles/{roleId}/members", "identityListHostRoleMembers", IdentityHostRolesTag),
        new("PUT", "/api/v1/identity/roles/{roleId}/members", "identityReplaceHostRoleMembers", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/roles/{roleId}/data-scope", "identityGetHostRoleDataScope", IdentityHostRolesTag),
        new("PUT", "/api/v1/identity/roles/{roleId}/data-scope", "identityUpdateHostRoleDataScope", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/roles/{roleId}/field-grants", "identityGetHostRoleFieldGrants", IdentityHostRolesTag),
        new("PUT", "/api/v1/identity/roles/{roleId}/field-grants", "identityReplaceHostRoleFieldGrants", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/menus", "identityListHostMenus", IdentityHostMenusTag),
        new("GET", "/api/v1/identity/menus/all", "identityListAllHostMenus", IdentityHostMenusTag),
        new("GET", "/api/v1/identity/menus/permission-options", "identityListHostMenuPermissionOptions", IdentityHostMenusTag),
        new("POST", "/api/v1/identity/menus/sync-catalog", "identitySyncHostMenuCatalog", IdentityHostMenusTag),
        new("GET", "/api/v1/identity/menus/{menuId}", "identityGetHostMenu", IdentityHostMenusTag),
        new("POST", "/api/v1/identity/menus", "identityCreateHostMenu", IdentityHostMenusTag),
        new("PUT", "/api/v1/identity/menus/{menuId}", "identityUpdateHostMenu", IdentityHostMenusTag),
        new("POST", "/api/v1/identity/menus/{menuId}/disable", "identityDisableHostMenu", IdentityHostMenusTag),
        new("POST", "/api/v1/identity/menus/{menuId}/enable", "identityEnableHostMenu", IdentityHostMenusTag),
        new("GET", "/api/v1/identity/api-keys", "identityListHostApiKeys", IdentityHostApiKeysTag),
        new("POST", "/api/v1/identity/api-keys", "identityCreateHostApiKey", IdentityHostApiKeysTag),
        new("POST", "/api/v1/identity/api-keys/{apiKeyId}/disable", "identityDisableHostApiKey", IdentityHostApiKeysTag),
        new("POST", "/api/v1/identity/api-keys/{apiKeyId}/rotate", "identityRotateHostApiKey", IdentityHostApiKeysTag),
        new("GET", "/api/v1/identity/open-access-clients", "identityListOpenAccessClients", IdentityOpenAccessClientsTag),
        new("GET", "/api/v1/identity/open-access-clients/{clientId}", "identityGetOpenAccessClient", IdentityOpenAccessClientsTag),
        new("POST", "/api/v1/identity/open-access-clients", "identityCreateOpenAccessClient", IdentityOpenAccessClientsTag),
        new("PUT", "/api/v1/identity/open-access-clients/{clientId}", "identityUpdateOpenAccessClient", IdentityOpenAccessClientsTag),
        new("POST", "/api/v1/identity/open-access-clients/{clientId}/disable", "identityDisableOpenAccessClient", IdentityOpenAccessClientsTag),
        new("POST", "/api/v1/identity/open-access-clients/{clientId}/rotate", "identityRotateOpenAccessClient", IdentityOpenAccessClientsTag),
        new("GET", "/api/v1/identity/open-access-clients/{clientId}/access-logs", "identityListOpenAccessClientAccessLogs", IdentityOpenAccessClientsTag),
        new("GET", "/api/v1/identity/open-access-clients/{clientId}/usage", "identityGetOpenAccessClientUsage", IdentityOpenAccessClientsTag),
        new("POST", "/api/v1/identity/open-access-clients/{clientId}/signature-debug", "identityDebugOpenAccessClientSignature", IdentityOpenAccessClientsTag),
        new("GET", "/api/v1/identity/registration-policy", "identityGetRegistrationPolicy", IdentityRegistrationPolicyTag),
        new("PUT", "/api/v1/identity/registration-policy", "identityUpdateRegistrationPolicy", IdentityRegistrationPolicyTag),
        new("GET", "/api/v1/identity/registration-ways", "identityListRegistrationWays", IdentityRegistrationWaysTag),
        new("GET", "/api/v1/identity/registration-ways/{wayId}", "identityGetRegistrationWay", IdentityRegistrationWaysTag),
        new("POST", "/api/v1/identity/registration-ways", "identityCreateRegistrationWay", IdentityRegistrationWaysTag),
        new("PUT", "/api/v1/identity/registration-ways/{wayId}", "identityUpdateRegistrationWay", IdentityRegistrationWaysTag),
        new("DELETE", "/api/v1/identity/registration-ways/{wayId}", "identityDeleteRegistrationWay", IdentityRegistrationWaysTag),
        new("GET", "/api/v1/identity/ldap-connections", "identityListLdapConnections", IdentityLdapConnectionsTag),
        new("GET", "/api/v1/identity/ldap-connections/{connectionId}", "identityGetLdapConnection", IdentityLdapConnectionsTag),
        new("POST", "/api/v1/identity/ldap-connections", "identityCreateLdapConnection", IdentityLdapConnectionsTag),
        new("PUT", "/api/v1/identity/ldap-connections/{connectionId}", "identityUpdateLdapConnection", IdentityLdapConnectionsTag),
        new("DELETE", "/api/v1/identity/ldap-connections/{connectionId}", "identityDeleteLdapConnection", IdentityLdapConnectionsTag),
        new("POST", "/api/v1/identity/ldap-connections/{connectionId}/disable", "identityDisableLdapConnection", IdentityLdapConnectionsTag),
        new("POST", "/api/v1/identity/ldap-connections/{connectionId}/test-connection", "identityTestLdapConnection", IdentityLdapConnectionsTag),
        new("POST", "/api/v1/identity/ldap-connections/{connectionId}/test-authentication", "identityTestLdapAuthentication", IdentityLdapConnectionsTag),
        new("POST", "/api/v1/identity/ldap-connections/{connectionId}/preview-sync", "identityPreviewLdapSync", IdentityLdapConnectionsTag),
        new("GET", "/api/v1/identity/oauth-providers", "identityListOAuthProviders", IdentityOAuthProvidersTag),
        new("GET", "/api/v1/identity/oauth-providers/{providerId}", "identityGetOAuthProvider", IdentityOAuthProvidersTag),
        new("POST", "/api/v1/identity/oauth-providers", "identityCreateOAuthProvider", IdentityOAuthProvidersTag),
        new("PUT", "/api/v1/identity/oauth-providers/{providerId}", "identityUpdateOAuthProvider", IdentityOAuthProvidersTag),
        new("DELETE", "/api/v1/identity/oauth-providers/{providerId}", "identityDeleteOAuthProvider", IdentityOAuthProvidersTag),
        new("GET", "/api/v1/identity/oauth/providers", "identityListPublicOAuthProviders", IdentityOAuthPublicTag),
        new("GET", "/api/v1/identity/oauth/{providerKey}/authorize", "identityBeginOAuthAuthorization", IdentityOAuthPublicTag),
        new("GET", "/api/v1/identity/oauth/callback", "identityOAuthCallback", IdentityOAuthPublicTag),
        new("GET", "/api/v1/identity/me/oauth-links", "identityListOAuthUserLinks", IdentityOAuthLinksTag),
        new("DELETE", "/api/v1/identity/me/oauth-links/{linkId}", "identityDeleteOAuthUserLink", IdentityOAuthLinksTag),
        new("GET", "/api/v1/identity/public/registration-ways", "identityListPublicRegistrationWays", IdentityPublicRegistrationTag),
        new("GET", "/api/v1/identity/session-policy", "identityGetHostSessionPolicy", IdentityHostOnlineSessionsTag),
        new("GET", "/api/v1/identity/online-sessions", "identityListHostOnlineSessions", IdentityHostOnlineSessionsTag),
        new("POST", "/api/v1/identity/online-sessions/users/{userId}/revoke-all", "identityRevokeAllHostUserOnlineSessions", IdentityHostOnlineSessionsTag),
        new("POST", "/api/v1/identity/online-sessions/{sessionId}/revoke", "identityRevokeHostOnlineSession", IdentityHostOnlineSessionsTag),
        new("GET", "/api/v1/identity/modules", "identityListHostModules", IdentityHostModulesTag),
        new("GET", "/api/v1/identity/modules/{moduleKey}", "identityGetHostModule", IdentityHostModulesTag),
        new("GET", "/api/v1/identity/modules/selection/runtime", "identityGetModuleSelectionRuntime", IdentityHostModulesTag),
        new("POST", "/api/v1/identity/modules/selection/validate", "identityValidateModuleSelection", IdentityHostModulesTag),
        new("GET", "/api/v1/me", "identityGetCurrentUser", IdentityMeTag),
        new("GET", "/api/v1/me/profile", "identityGetSelfServiceProfile", IdentityMeTag),
        new("PUT", "/api/v1/me/profile", "identityUpdateSelfServiceProfile", IdentityMeTag),
        new("POST", "/api/v1/me/profile/avatar", "identityUploadSelfServiceAvatar", IdentityMeTag),
        new("DELETE", "/api/v1/me/profile/avatar", "identityDeleteSelfServiceAvatar", IdentityMeTag),
        new("GET", "/api/v1/me/profile/avatar/content", "identityGetSelfServiceAvatarContent", IdentityMeTag),
        new("POST", "/api/v1/me/profile/signature", "identityUploadSelfServiceSignature", IdentityMeTag),
        new("DELETE", "/api/v1/me/profile/signature", "identityDeleteSelfServiceSignature", IdentityMeTag),
        new("GET", "/api/v1/me/profile/signature/content", "identityGetSelfServiceSignatureContent", IdentityMeTag),
        new("POST", "/api/v1/me/password", "identityChangePassword", IdentityMeTag),
        new("GET", "/api/v1/identity/me/mfa/totp", "identityGetTotpEnrollmentStatus", IdentityTotpEnrollmentTag),
        new("POST", "/api/v1/identity/me/mfa/totp/begin", "identityBeginTotpEnrollment", IdentityTotpEnrollmentTag),
        new("POST", "/api/v1/identity/me/mfa/totp/confirm", "identityConfirmTotpEnrollment", IdentityTotpEnrollmentTag),
        new("GET", "/api/v1/identity/super-administrators", "identityListSuperAdministrators", IdentitySuperAdministratorsTag),
        new("GET", "/api/v1/identity/super-administrators/audits", "identityListSuperAdministratorAudits", IdentitySuperAdministratorsTag),
        new("POST", "/api/v1/identity/super-administrators/grant", "identityGrantSuperAdministrator", IdentitySuperAdministratorsTag),
        new("POST", "/api/v1/identity/super-administrators/{targetUserId}/revoke", "identityRevokeSuperAdministrator", IdentitySuperAdministratorsTag),
        new("POST", "/api/v1/auth/login", "identityLogin", IdentityAuthSessionTag),
        new("POST", "/api/v1/auth/refresh", "identityRefreshSession", IdentityAuthSessionTag),
        new("POST", "/api/v1/auth/logout", "identityLogout", IdentityAuthSessionTag),
        new("PUT", "/api/v1/me/locale", "identityUpdatePreferredLocale", IdentityAuthSessionTag),
        new("GET", "/api/v1/tenancy/tenants", "tenancyListHostTenants", TenancyHostTenantsTag),
        new("GET", "/api/v1/tenancy/tenants/{tenantId}", "tenancyGetHostTenant", TenancyHostTenantsTag),
        new("POST", "/api/v1/tenancy/tenants", "tenancyCreateHostTenant", TenancyHostTenantsTag),
        new("PUT", "/api/v1/tenancy/tenants/{tenantId}", "tenancyUpdateHostTenant", TenancyHostTenantsTag),
        new("POST", "/api/v1/tenancy/tenants/{tenantId}/disable", "tenancyDisableHostTenant", TenancyHostTenantsTag),
        new("POST", "/api/v1/tenancy/tenants/{tenantId}/enable", "tenancyEnableHostTenant", TenancyHostTenantsTag),
        new("GET", "/api/v1/tenancy/tenants/{tenantId}/members", "tenancyListHostTenantMembers", TenancyHostTenantsTag),
        new("GET", "/api/v1/tenancy/tenants/{tenantId}/administrators", "tenancyListHostTenantAdministrators", TenancyHostTenantsTag),
        new("POST", "/api/v1/tenancy/tenants/{tenantId}/package", "tenancyAssignHostTenantPackage", TenancyHostTenantsTag),
        new("GET", "/api/v1/tenancy/tenants/{tenantId}/branding", "tenancyGetHostTenantBranding", TenancyHostTenantsTag),
        new("PUT", "/api/v1/tenancy/tenants/{tenantId}/branding", "tenancyUpdateHostTenantBranding", TenancyHostTenantsTag),
        new("POST", "/api/v1/tenancy/tenants/{tenantId}/branding/logo", "tenancyUploadHostTenantBrandingLogo", TenancyHostTenantsTag),
        new("DELETE", "/api/v1/tenancy/tenants/{tenantId}/branding/logo", "tenancyDeleteHostTenantBrandingLogo", TenancyHostTenantsTag),
        new("GET", "/api/v1/tenancy/tenants/{tenantId}/branding/logo/content", "tenancyGetHostTenantBrandingLogoContent", TenancyHostTenantsTag),
        new("GET", "/api/v1/tenancy/tenant-packages", "tenancyListHostTenantPackages", TenancyHostTenantPackagesTag),
        new("GET", "/api/v1/tenancy/tenant-packages/{packageId}", "tenancyGetHostTenantPackage", TenancyHostTenantPackagesTag),
        new("POST", "/api/v1/tenancy/tenant-packages", "tenancyCreateHostTenantPackage", TenancyHostTenantPackagesTag),
        new("PUT", "/api/v1/tenancy/tenant-packages/{packageId}", "tenancyUpdateHostTenantPackage", TenancyHostTenantPackagesTag),
        new("POST", "/api/v1/tenancy/tenant-packages/{packageId}/disable", "tenancyDisableHostTenantPackage", TenancyHostTenantPackagesTag),
        new("GET", "/api/v1/tenancy/branding/current", "tenancyGetRuntimeBranding", "Tenancy"),
        new("GET", "/api/v1/tenancy/branding", "tenancyGetCurrentBranding", "Tenancy"),
        new("PUT", "/api/v1/tenancy/branding", "tenancyUpdateCurrentBranding", "Tenancy"),
        new("POST", "/api/v1/tenancy/branding/logo", "tenancyUploadCurrentBrandingLogo", "Tenancy"),
        new("DELETE", "/api/v1/tenancy/branding/logo", "tenancyDeleteCurrentBrandingLogo", "Tenancy"),
        new("GET", "/api/v1/tenancy/branding/logo/content", "tenancyGetCurrentBrandingLogoContent", "Tenancy"),
        new("GET", "/api/v1/organization/units", "organizationListTenantUnits", OrganizationTenantUnitsTag),
        new("GET", "/api/v1/organization/units/{unitId}", "organizationGetTenantUnit", OrganizationTenantUnitsTag),
        new("POST", "/api/v1/organization/units", "organizationCreateTenantUnit", OrganizationTenantUnitsTag),
        new("PUT", "/api/v1/organization/units/{unitId}", "organizationUpdateTenantUnit", OrganizationTenantUnitsTag),
        new("POST", "/api/v1/organization/units/{unitId}/disable", "organizationDisableTenantUnit", OrganizationTenantUnitsTag),
        new("GET", "/api/v1/organization/user-units/assignable-users", "organizationListAssignableTenantUserUnitUsers", OrganizationTenantUserUnitsTag),
        new("GET", "/api/v1/organization/user-units", "organizationListTenantUserUnits", OrganizationTenantUserUnitsTag),
        new("POST", "/api/v1/organization/user-units", "organizationCreateTenantUserUnit", OrganizationTenantUserUnitsTag),
        new("PUT", "/api/v1/organization/user-units/{assignmentId}", "organizationUpdateTenantUserUnit", OrganizationTenantUserUnitsTag),
        new("POST", "/api/v1/organization/user-units/{assignmentId}/disable", "organizationDisableTenantUserUnit", OrganizationTenantUserUnitsTag),
        new("GET", "/api/v1/organization/positions", "organizationListTenantPositions", OrganizationTenantPositionsTag),
        new("GET", "/api/v1/organization/positions/{positionId}", "organizationGetTenantPosition", OrganizationTenantPositionsTag),
        new("POST", "/api/v1/organization/positions", "organizationCreateTenantPosition", OrganizationTenantPositionsTag),
        new("PUT", "/api/v1/organization/positions/{positionId}", "organizationUpdateTenantPosition", OrganizationTenantPositionsTag),
        new("PUT", "/api/v1/organization/positions/{positionId}/unit", "organizationAssignTenantPositionUnit", OrganizationTenantPositionsTag),
        new("PUT", "/api/v1/organization/positions/{positionId}/position-level", "organizationAssignTenantPositionLevel", OrganizationTenantPositionsTag),
        new("POST", "/api/v1/organization/positions/{positionId}/disable", "organizationDisableTenantPosition", OrganizationTenantPositionsTag),
        new("GET", "/api/v1/organization/positions/export-file", "organizationExportTenantPositionsWorkbook", OrganizationTenantPositionsTag),
        new("GET", "/api/v1/organization/positions/import-template", "organizationDownloadTenantPositionImportTemplate", OrganizationTenantPositionsTag),
        new("POST", "/api/v1/organization/positions/import", "organizationImportTenantPositions", OrganizationTenantPositionsTag),
        new("POST", "/api/v1/organization/positions/import-file", "organizationImportTenantPositionsWorkbook", OrganizationTenantPositionsTag),
        new("GET", "/api/v1/organization/position-levels", "organizationListTenantPositionLevels", OrganizationTenantPositionLevelsTag),
        new("GET", "/api/v1/organization/position-levels/{positionLevelId}", "organizationGetTenantPositionLevel", OrganizationTenantPositionLevelsTag),
        new("POST", "/api/v1/organization/position-levels", "organizationCreateTenantPositionLevel", OrganizationTenantPositionLevelsTag),
        new("PUT", "/api/v1/organization/position-levels/{positionLevelId}", "organizationUpdateTenantPositionLevel", OrganizationTenantPositionLevelsTag),
        new("POST", "/api/v1/organization/position-levels/{positionLevelId}/disable", "organizationDisableTenantPositionLevel", OrganizationTenantPositionLevelsTag),
        new("GET", "/api/v1/organization/user-positions/assignable-users", "organizationListAssignableTenantUserPositionUsers", OrganizationTenantUserPositionsTag),
        new("GET", "/api/v1/organization/user-positions", "organizationListTenantUserPositions", OrganizationTenantUserPositionsTag),
        new("POST", "/api/v1/organization/user-positions", "organizationCreateTenantUserPosition", OrganizationTenantUserPositionsTag),
        new("PUT", "/api/v1/organization/user-positions/{assignmentId}", "organizationUpdateTenantUserPosition", OrganizationTenantUserPositionsTag),
        new("POST", "/api/v1/organization/user-positions/{assignmentId}/disable", "organizationDisableTenantUserPosition", OrganizationTenantUserPositionsTag),
        new("GET", "/api/v1/organization/host-user-management/reference", "organizationGetHostUserManagementReference", OrganizationHostUserManagementTag),
        new("POST", "/api/v1/organization/host-user-management/user-units", "organizationCreateHostUserManagementUserUnit", OrganizationHostUserManagementTag),
        new("PUT", "/api/v1/organization/host-user-management/user-units/{assignmentId}", "organizationUpdateHostUserManagementUserUnit", OrganizationHostUserManagementTag),
        new("POST", "/api/v1/organization/host-user-management/user-units/{assignmentId}/disable", "organizationDisableHostUserManagementUserUnit", OrganizationHostUserManagementTag),
        new("POST", "/api/v1/organization/host-user-management/user-positions", "organizationCreateHostUserManagementUserPosition", OrganizationHostUserManagementTag),
        new("PUT", "/api/v1/organization/host-user-management/user-positions/{assignmentId}", "organizationUpdateHostUserManagementUserPosition", OrganizationHostUserManagementTag),
        new("POST", "/api/v1/organization/host-user-management/user-positions/{assignmentId}/disable", "organizationDisableHostUserManagementUserPosition", OrganizationHostUserManagementTag),
        new("GET", "/api/v1/files/host-files", "filesListHostFiles", FilesTag),
        new("GET", "/api/v1/files/host-files/{fileId}", "filesGetHostFile", FilesTag),
        new("POST", "/api/v1/files/host-files", "filesUploadHostFile", FilesTag),
        new("POST", "/api/v1/files/host-files/batch-upload", "filesBatchUploadHostFiles", FilesTag),
        new("POST", "/api/v1/files/host-files/batch-delete", "filesBatchDeleteHostFiles", FilesTag),
        new("POST", "/api/v1/files/host-files/{fileId}/update", "filesUpdateHostFileMetadata", FilesTag),
        new("GET", "/api/v1/files/host-files/{fileId}/references", "filesListHostFileReferences", FilesTag),
        new("GET", "/api/v1/files/host-files/{fileId}/content", "filesDownloadHostFileContent", FilesTag),
        new("GET", "/api/v1/files/host-files/{fileId}/preview", "filesPreviewHostFileContent", FilesTag),
        new("POST", "/api/v1/files/host-files/{fileId}/delete", "filesDeleteHostFile", FilesTag),
        new("GET", "/api/v1/files/host-folders/tree", "filesGetHostFolderTree", FilesFoldersTag),
        new("POST", "/api/v1/files/host-folders", "filesCreateHostFolder", FilesFoldersTag),
        new("POST", "/api/v1/files/host-folders/{folderId}/update", "filesUpdateHostFolder", FilesFoldersTag),
        new("POST", "/api/v1/files/host-folders/{folderId}/delete", "filesDeleteHostFolder", FilesFoldersTag),
        new("GET", "/api/v1/files/storage-providers", "filesListStorageProviders", FilesStorageProvidersTag),
        new("POST", "/api/v1/files/storage-providers/{providerKey}/test", "filesTestStorageProviderConnectivity", FilesStorageProvidersTag),
        new("GET", "/api/v1/settings/config-entries", "settingsListHostConfigEntries", SettingsTag),
        new("GET", "/api/v1/settings/config-entries/by-key/{configKey}", "settingsGetHostConfigEntryByKey", SettingsTag),
        new("GET", "/api/v1/settings/config-entries/{configEntryId}", "settingsGetHostConfigEntry", SettingsTag),
        new("POST", "/api/v1/settings/config-entries", "settingsCreateHostConfigEntry", SettingsTag),
        new("PUT", "/api/v1/settings/config-entries/{configEntryId}", "settingsUpdateHostConfigEntry", SettingsTag),
        new("POST", "/api/v1/settings/config-entries/{configEntryId}/disable", "settingsDisableHostConfigEntry", SettingsTag),
        new("POST", "/api/v1/settings/config-entries/{configEntryId}/delete", "settingsDeleteHostConfigEntry", SettingsTag),
        new("POST", "/api/v1/settings/config-entries/batch-delete", "settingsBatchDeleteHostConfigEntries", SettingsTag),
        new("POST", "/api/v1/settings/config-entries/batch-update-values", "settingsBatchUpdateHostConfigEntryValues", SettingsTag),
        new("GET", "/api/v1/settings/config-entries/list", "settingsListAllHostConfigEntries", SettingsTag),
        new("GET", "/api/v1/settings/config-entries/groups", "settingsListHostConfigEntryGroups", SettingsTag),
        new("GET", "/api/v1/settings/diagnostic-policy", "settingsGetHostDiagnosticPolicy", SettingsDiagnosticPolicyTag),
        new("PUT", "/api/v1/settings/diagnostic-policy", "settingsUpdateHostDiagnosticPolicy", SettingsDiagnosticPolicyTag),
        new("POST", "/api/v1/settings/diagnostic-policy/restore", "settingsRestoreHostDiagnosticPolicy", SettingsDiagnosticPolicyTag),
        new("GET", "/api/v1/settings/dict-types", "settingsListHostDictTypes", SettingsHostDictTypesTag),
        new("POST", "/api/v1/settings/dict-types", "settingsCreateHostDictType", SettingsHostDictTypesTag),
        new("PUT", "/api/v1/settings/dict-types/{dictTypeId}", "settingsUpdateHostDictType", SettingsHostDictTypesTag),
        new("POST", "/api/v1/settings/dict-types/{dictTypeId}/disable", "settingsDisableHostDictType", SettingsHostDictTypesTag),
        new("POST", "/api/v1/settings/dict-types/{dictTypeId}/delete", "settingsDeleteHostDictType", SettingsHostDictTypesTag),
        new("GET", "/api/v1/settings/dict-types/list", "settingsListAllHostDictTypes", SettingsHostDictTypesTag),
        new("GET", "/api/v1/settings/dict-types/by-code/{code}/items", "settingsListHostDictItemsByTypeCode", SettingsHostDictTypesTag),
        new("GET", "/api/v1/settings/dict-types/{dictTypeId}/items", "settingsListHostDictItems", SettingsHostDictTypesTag),
        new("POST", "/api/v1/settings/dict-types/{dictTypeId}/items", "settingsCreateHostDictItem", SettingsHostDictTypesTag),
        new("GET", "/api/v1/settings/dict-items/{dictItemId}", "settingsGetHostDictItem", SettingsHostDictTypesTag),
        new("PUT", "/api/v1/settings/dict-items/{dictItemId}", "settingsUpdateHostDictItem", SettingsHostDictTypesTag),
        new("POST", "/api/v1/settings/dict-items/{dictItemId}/disable", "settingsDisableHostDictItem", SettingsHostDictTypesTag),
        new("POST", "/api/v1/settings/dict-items/{dictItemId}/delete", "settingsDeleteHostDictItem", SettingsHostDictTypesTag),
        new("GET", "/api/v1/settings/enum-catalogs", "settingsListHostEnumCatalogs", SettingsHostEnumCatalogsTag),
        new("GET", "/api/v1/settings/enum-catalogs/{catalogKey}", "settingsGetHostEnumCatalog", SettingsHostEnumCatalogsTag),
        new("GET", "/api/v1/settings/enum-catalogs/{catalogKey}/dict-generation-preview", "settingsPreviewHostEnumCatalogDictGeneration", SettingsHostEnumCatalogsTag),
        new("POST", "/api/v1/settings/enum-catalogs/{catalogKey}/dict-generation", "settingsGenerateHostEnumCatalogDict", SettingsHostEnumCatalogsTag),
        new("GET", "/api/v1/settings/tenant-dict-types", "settingsListTenantDictTypes", SettingsTenantDictTypesTag),
        new("POST", "/api/v1/settings/tenant-dict-types", "settingsCreateTenantDictType", SettingsTenantDictTypesTag),
        new("PUT", "/api/v1/settings/tenant-dict-types/{dictTypeId}", "settingsUpdateTenantDictType", SettingsTenantDictTypesTag),
        new("POST", "/api/v1/settings/tenant-dict-types/{dictTypeId}/disable", "settingsDisableTenantDictType", SettingsTenantDictTypesTag),
        new("POST", "/api/v1/settings/tenant-dict-types/{dictTypeId}/delete", "settingsDeleteTenantDictType", SettingsTenantDictTypesTag),
        new("GET", "/api/v1/settings/tenant-dict-types/list", "settingsListAllTenantDictTypes", SettingsTenantDictTypesTag),
        new("GET", "/api/v1/settings/tenant-dict-types/by-code/{code}/items", "settingsListTenantDictItemsByTypeCode", SettingsTenantDictTypesTag),
        new("GET", "/api/v1/settings/tenant-dict-types/{dictTypeId}/items", "settingsListTenantDictItems", SettingsTenantDictTypesTag),
        new("POST", "/api/v1/settings/tenant-dict-types/{dictTypeId}/items", "settingsCreateTenantDictItem", SettingsTenantDictTypesTag),
        new("GET", "/api/v1/settings/tenant-dict-items/{dictItemId}", "settingsGetTenantDictItem", SettingsTenantDictTypesTag),
        new("PUT", "/api/v1/settings/tenant-dict-items/{dictItemId}", "settingsUpdateTenantDictItem", SettingsTenantDictTypesTag),
        new("POST", "/api/v1/settings/tenant-dict-items/{dictItemId}/disable", "settingsDisableTenantDictItem", SettingsTenantDictTypesTag),
        new("POST", "/api/v1/settings/tenant-dict-items/{dictItemId}/delete", "settingsDeleteTenantDictItem", SettingsTenantDictTypesTag),
        new("GET", "/api/v1/auditing/access-logs", "auditingListHostAccessLogs", AuditingHostAccessLogsTag),
        new("GET", "/api/v1/auditing/access-logs/cursor", "auditingListHostAccessLogsByCursor", AuditingHostAccessLogsTag),
        new("GET", "/api/v1/auditing/access-logs/trends", "auditingQueryHostAccessLogTrends", AuditingHostAccessLogsTag),
        new("GET", "/api/v1/auditing/operation-logs", "auditingListHostOperationLogs", AuditingHostOperationLogsTag),
        new("GET", "/api/v1/auditing/operation-logs/trends", "auditingQueryHostOperationLogTrends", AuditingHostOperationLogsTag),
        new("GET", "/api/v1/auditing/exception-logs", "auditingListHostExceptionLogs", AuditingHostExceptionLogsTag),
        new("GET", "/api/v1/auditing/exception-logs/trends", "auditingQueryHostExceptionLogTrends", AuditingHostExceptionLogsTag),
        new("GET", "/api/v1/auditing/domain-change-diffs", "auditingQueryDomainChangeDiffs", "AuditingDomainChangeDiffs"),
        new("POST", "/api/v1/auditing/access-logs/exports", "auditingExportHostAccessLogs", AuditingHostAccessLogsTag),
        new("POST", "/api/v1/auditing/operation-logs/exports", "auditingExportHostOperationLogs", AuditingHostOperationLogsTag),
        new("POST", "/api/v1/auditing/exception-logs/exports", "auditingExportHostExceptionLogs", AuditingHostExceptionLogsTag),
        new("GET", "/api/v1/auditing/outbound-call-logs", "auditingListHostOutboundCallLogs", AuditingHostOutboundCallLogsTag),
        new("GET", "/api/v1/platform/host-dashboard-summary", "platformGetHostDashboardSummary", PlatformHostDashboardTag),
        new("GET", "/api/v1/jobs/host-definitions", "jobsListHostJobDefinitions", JobsHostJobDefinitionsTag),
        new("GET", "/api/v1/jobs/host-definitions/groups", "jobsListHostJobGroups", JobsHostJobDefinitionsTag),
        new("POST", "/api/v1/jobs/host-definitions", "jobsCreateHostJobDefinition", JobsHostJobDefinitionsTag),
        new("PUT", "/api/v1/jobs/host-definitions/{definitionId}", "jobsUpdateHostJobDefinition", JobsHostJobDefinitionsTag),
        new("POST", "/api/v1/jobs/host-definitions/{definitionId}/disable", "jobsDisableHostJobDefinition", JobsHostJobDefinitionsTag),
        new("POST", "/api/v1/jobs/host-definitions/{definitionId}/delete", "jobsDeleteHostJobDefinition", JobsHostJobDefinitionsTag),
        new("POST", "/api/v1/jobs/host-definitions/{definitionId}/trigger", "jobsTriggerHostJobDefinition", JobsHostJobDefinitionsTag),
        new("GET", "/api/v1/jobs/host-executions", "jobsListHostJobExecutions", JobsHostJobExecutionsTag),
        new("GET", "/api/v1/jobs/host-executions/{executionId}", "jobsGetHostJobExecution", JobsHostJobExecutionsTag),
        new("POST", "/api/v1/jobs/host-executions/{executionId}/cancel", "jobsCancelHostJobExecution", JobsHostJobExecutionsTag),
        new("POST", "/api/v1/jobs/host-executions/clear", "jobsClearHostJobExecutions", JobsHostJobExecutionsTag),
        new("GET", "/api/v1/jobs/host-schedules", "jobsListHostJobSchedules", JobsHostJobSchedulesTag),
        new("GET", "/api/v1/jobs/host-schedules/definition-options", "jobsListHostJobScheduleDefinitionOptions", JobsHostJobSchedulesTag),
        new("GET", "/api/v1/jobs/host-schedules/cron-preview", "jobsPreviewHostJobScheduleCron", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules", "jobsCreateHostJobSchedule", JobsHostJobSchedulesTag),
        new("PUT", "/api/v1/jobs/host-schedules/{scheduleId}", "jobsUpdateHostJobSchedule", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules/batch-pause", "jobsBatchPauseHostJobSchedules", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules/batch-resume", "jobsBatchResumeHostJobSchedules", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules/{scheduleId}/pause", "jobsPauseHostJobSchedule", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules/{scheduleId}/resume", "jobsResumeHostJobSchedule", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules/{scheduleId}/delete", "jobsDeleteHostJobSchedule", JobsHostJobSchedulesTag),
        new("GET", "/api/v1/jobs/host-health", "jobsGetHostJobHealth", JobsHostJobHealthTag),
        new("GET", "/api/v1/calendar/my-personal-schedules", "calendarListMyPersonalSchedules", CalendarMyPersonalSchedulesTag),
        new("GET", "/api/v1/calendar/my-personal-schedules/{scheduleId}", "calendarGetMyPersonalSchedule", CalendarMyPersonalSchedulesTag),
        new("POST", "/api/v1/calendar/my-personal-schedules", "calendarCreateMyPersonalSchedule", CalendarMyPersonalSchedulesTag),
        new("PUT", "/api/v1/calendar/my-personal-schedules/{scheduleId}", "calendarUpdateMyPersonalSchedule", CalendarMyPersonalSchedulesTag),
        new("POST", "/api/v1/calendar/my-personal-schedules/{scheduleId}/delete", "calendarDeleteMyPersonalSchedule", CalendarMyPersonalSchedulesTag),
        new("POST", "/api/v1/calendar/my-personal-schedules/{scheduleId}/status", "calendarSetMyPersonalScheduleStatus", CalendarMyPersonalSchedulesTag),
        new("GET", "/api/v1/platform/host-release-notes", "platformListHostReleaseNotes", PlatformHostReleaseNotesTag),
        new("GET", "/api/v1/platform/host-release-notes/{releaseNoteId}", "platformGetHostReleaseNote", PlatformHostReleaseNotesTag),
        new("POST", "/api/v1/platform/host-release-notes", "platformCreateHostReleaseNote", PlatformHostReleaseNotesTag),
        new("PUT", "/api/v1/platform/host-release-notes/{releaseNoteId}", "platformUpdateHostReleaseNote", PlatformHostReleaseNotesTag),
        new("POST", "/api/v1/platform/host-release-notes/{releaseNoteId}/publish", "platformPublishHostReleaseNote", PlatformHostReleaseNotesTag),
        new("POST", "/api/v1/platform/host-release-notes/{releaseNoteId}/retract", "platformRetractHostReleaseNote", PlatformHostReleaseNotesTag),
        new("POST", "/api/v1/platform/host-release-notes/{releaseNoteId}/delete", "platformDeleteHostReleaseNote", PlatformHostReleaseNotesTag),
        new("GET", "/api/v1/platform/my-release-notes", "platformListMyReleaseNotes", PlatformMyReleaseNotesTag),
        new("GET", "/api/v1/platform/my-release-notes/latest-unread", "platformGetLatestUnreadReleaseNote", PlatformMyReleaseNotesTag),
        new("POST", "/api/v1/platform/my-release-notes/{releaseNoteId}/read", "platformMarkMyReleaseNoteRead", PlatformMyReleaseNotesTag),
        new("GET", "/api/v1/platform/backup-executor/status", "platformGetBackupExecutorStatus", PlatformBackupExecutorTag),
        new("GET", "/api/v1/platform/backup-executor/tasks", "platformListBackupTasks", PlatformBackupExecutorTag),
        new("GET", "/api/v1/platform/backup-executor/tasks/{taskId}", "platformGetBackupTask", PlatformBackupExecutorTag),
        new("GET", "/api/v1/platform/backup-executor/runs", "platformListBackupRuns", PlatformBackupExecutorTag),
        new("GET", "/api/v1/platform/backup-executor/runs/{runId}", "platformGetBackupRun", PlatformBackupExecutorTag),
        new("GET", "/api/v1/platform/backup-executor/runs/{runId}/download", "platformDownloadBackupRunArtifact", PlatformBackupExecutorTag),
        new("GET", "/api/v1/regions/administrative-regions/children", "regionsListAdministrativeRegionChildren", RegionsAdministrativeRegionsTag),
        new("GET", "/api/v1/regions/administrative-regions/tree", "regionsGetAdministrativeRegionTree", RegionsAdministrativeRegionsTag),
        new("GET", "/api/v1/regions/administrative-regions", "regionsListAdministrativeRegions", RegionsAdministrativeRegionsTag),
        new("POST", "/api/v1/regions/administrative-regions", "regionsCreateAdministrativeRegion", RegionsAdministrativeRegionsTag),
        new("GET", "/api/v1/regions/administrative-regions/dataset-manifest/latest", "regionsGetLatestAdministrativeRegionDatasetManifest", RegionsAdministrativeRegionsTag),
        new("GET", "/api/v1/regions/administrative-regions/{regionId}", "regionsGetAdministrativeRegion", RegionsAdministrativeRegionsTag),
        new("PUT", "/api/v1/regions/administrative-regions/{regionId}", "regionsUpdateAdministrativeRegion", RegionsAdministrativeRegionsTag),
        new("POST", "/api/v1/regions/administrative-regions/{regionId}/delete", "regionsDeleteAdministrativeRegion", RegionsAdministrativeRegionsTag),
        new("POST", "/api/v1/regions/administrative-regions/import/preview", "regionsPreviewAdministrativeRegionImport", RegionsAdministrativeRegionsTag),
        new("POST", "/api/v1/regions/administrative-regions/import/apply", "regionsApplyAdministrativeRegionImport", RegionsAdministrativeRegionsTag),
        new("GET", "/api/v1/notifications/host-announcements", "notificationsListHostAnnouncements", NotificationsHostAnnouncementsTag),
        new("POST", "/api/v1/notifications/host-announcements", "notificationsCreateHostAnnouncement", NotificationsHostAnnouncementsTag),
        new("PUT", "/api/v1/notifications/host-announcements/{announcementId}", "notificationsUpdateHostAnnouncement", NotificationsHostAnnouncementsTag),
        new("POST", "/api/v1/notifications/host-announcements/{announcementId}/publish", "notificationsPublishHostAnnouncement", NotificationsHostAnnouncementsTag),
        new("POST", "/api/v1/notifications/host-announcements/{announcementId}/retract", "notificationsRetractHostAnnouncement", NotificationsHostAnnouncementsTag),
        new("GET", "/api/v1/notifications/host-announcements/{announcementId}/read-stats", "notificationsGetHostAnnouncementReadStats", NotificationsHostAnnouncementsTag),
        new("GET", "/api/v1/notifications/host-announcements/{announcementId}/read-receipts", "notificationsListHostAnnouncementReadReceipts", NotificationsHostAnnouncementsTag),
        new("GET", "/api/v1/notifications/my-host-announcements", "notificationsListMyHostAnnouncements", NotificationsMyHostAnnouncementsTag),
        new("GET", "/api/v1/notifications/my-host-announcements/unread-count", "notificationsGetMyHostAnnouncementUnreadCount", NotificationsMyHostAnnouncementsTag),
        new("GET", "/api/v1/notifications/my-host-announcements/{announcementId}", "notificationsGetMyHostAnnouncement", NotificationsMyHostAnnouncementsTag),
        new("POST", "/api/v1/notifications/my-host-announcements/{announcementId}/read", "notificationsMarkMyHostAnnouncementRead", NotificationsMyHostAnnouncementsTag),
        new("POST", "/api/v1/notifications/my-host-announcements/read-all", "notificationsMarkAllMyHostAnnouncementsRead", NotificationsMyHostAnnouncementsTag),
        new("GET", "/api/v1/notifications/my-inbox-messages", "notificationsListMyInboxMessages", NotificationsMyInboxMessagesTag),
        new("GET", "/api/v1/notifications/my-inbox-messages/unread-count", "notificationsGetMyInboxUnreadCount", NotificationsMyInboxMessagesTag),
        new("POST", "/api/v1/notifications/my-inbox-messages/{messageId}/read", "notificationsMarkMyInboxMessageRead", NotificationsMyInboxMessagesTag),
        new("POST", "/api/v1/notifications/my-inbox-messages/read-all", "notificationsMarkAllMyInboxMessagesRead", NotificationsMyInboxMessagesTag),
        new("POST", "/api/v1/notifications/host-inbox-messages", "notificationsSendHostInboxMessage", NotificationsHostInboxMessagesTag),
        new("POST", "/api/v1/code-generation/previews", "codeGenerationPreviewCrud", CodeGenerationPreviewsTag),
        new("POST", "/api/v1/code-generation/runs/preview", "codeGenerationPreviewRun", CodeGenerationRunsTag),
        new("POST", "/api/v1/code-generation/runs/apply", "codeGenerationApplyRun", CodeGenerationRunsTag),
        new("POST", "/api/v1/code-generation/runs/rollback", "codeGenerationRollbackRun", CodeGenerationRunsTag),
        new("POST", "/api/v1/code-generation/runs/rollback-chain", "codeGenerationRollbackRunChain", CodeGenerationRunsTag),
        new("GET", "/api/v1/code-generation/runs", "codeGenerationListRuns", CodeGenerationRunsTag),
        new("GET", "/api/v1/code-generation/runs/{runId}/artifacts.zip", "codeGenerationDownloadRunArtifacts", CodeGenerationRunsTag),
        new("GET", "/api/v1/code-generation/templates", "codeGenerationListTemplates", CodeGenerationTemplatesTag),
        new("GET", "/api/v1/code-generation/templates/{templateId}", "codeGenerationGetTemplate", CodeGenerationTemplatesTag),
        new("POST", "/api/v1/code-generation/templates", "codeGenerationCreateTemplate", CodeGenerationTemplatesTag),
        new("PUT", "/api/v1/code-generation/templates/{templateId}", "codeGenerationUpdateTemplate", CodeGenerationTemplatesTag),
        new("POST", "/api/v1/code-generation/templates/{templateId}/delete", "codeGenerationDeleteTemplate", CodeGenerationTemplatesTag),
        new("GET", "/api/v1/code-generation/catalog/tables", "codeGenerationListCatalogTables", CodeGenerationCatalogTag),
        new("GET", "/api/v1/code-generation/catalog/views", "codeGenerationListCatalogViews", CodeGenerationCatalogTag),
        new("GET", "/api/v1/code-generation/catalog/objects", "codeGenerationListCatalogObjects", CodeGenerationCatalogTag),
        new("GET", "/api/v1/code-generation/catalog/objects/{objectName}/metadata", "codeGenerationGetCatalogMetadata", CodeGenerationCatalogTag),
        new("POST", "/api/v1/code-generation/catalog/migration-draft", "codeGenerationGenerateCatalogMigrationDraft", CodeGenerationCatalogTag),
        new("GET", "/api/v1/code-generation/catalog/tables/{tableName}/columns", "codeGenerationListCatalogColumns", CodeGenerationCatalogTag),
        new("POST", "/api/v1/code-generation/catalog/column-sync", "codeGenerationSyncCatalogColumns", CodeGenerationCatalogTag),
        new("GET", "/api/v1/data-approvals/requests", "dataApprovalsListRequests", DataApprovalRequestsTag),
        new("POST", "/api/v1/data-approvals/requests", "dataApprovalsCreateRequest", DataApprovalRequestsTag),
        new("GET", "/api/v1/data-approvals/requests/{requestId}", "dataApprovalsGetRequest", DataApprovalRequestsTag),
        new("POST", "/api/v1/data-approvals/requests/{requestId}/cancel", "dataApprovalsCancelRequest", DataApprovalRequestsTag),
        new("POST", "/api/v1/data-approvals/requests/{requestId}/retry", "dataApprovalsRetryRequest", DataApprovalRequestsTag),
        new("POST", "/api/v1/data-approvals/requests/{requestId}/retry-apply", "dataApprovalsRetryApplyRequest", DataApprovalRequestsTag),
        new("GET", "/api/v1/data-approvals/scenarios", "dataApprovalsListScenarios", DataApprovalScenariosTag),
        new("GET", "/api/v1/data-approvals/scenarios/{scenarioKey}", "dataApprovalsGetScenario", DataApprovalScenariosTag),
        new("PUT", "/api/v1/data-approvals/scenarios/{scenarioKey}", "dataApprovalsUpdateScenarioBinding", DataApprovalScenariosTag),
        new("GET", "/api/v1/serial-numbers/rules", "serialNumbersListRules", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules", "serialNumbersCreateRule", SerialNumbersHostRulesTag),
        new("PUT", "/api/v1/serial-numbers/rules/{ruleId}", "serialNumbersUpdateRule", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/{ruleId}/enable", "serialNumbersEnableRule", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/{ruleId}/disable", "serialNumbersDisableRule", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/preview", "serialNumbersPreviewSerialNumber", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/{ruleId}/update-approval-preview", "serialNumbersPreviewRuleUpdateApproval", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/{ruleId}/update-approval-requests", "serialNumbersSubmitRuleUpdateApproval", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/{ruleId}/disable-approval-preview", "serialNumbersPreviewRuleDisableApproval", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/{ruleId}/disable-approval-requests", "serialNumbersSubmitRuleDisableApproval", SerialNumbersHostRulesTag),
        new("GET", "/api/v1/observability/log-files", "observabilityListLogFiles", ObservabilityLogFilesTag),
        new("GET", "/api/v1/observability/log-files/{id}/tail", "observabilityTailLogFile", ObservabilityLogFilesTag),
        new("GET", "/api/v1/observability/log-files/{id}/download", "observabilityDownloadLogFile", ObservabilityLogFilesTag),
        new("GET", "/api/v1/observability/server-instances", "observabilityListServerInstances", ObservabilityServerMonitorTag),
        new("GET", "/api/v1/observability/server-instances/{instanceKey}/runtime", "observabilityGetServerRuntime", ObservabilityServerMonitorTag),
        new("GET", "/api/v1/observability/cache-policies", "observabilityListCachePolicies", ObservabilityCachePoliciesTag),
        new("GET", "/api/v1/observability/cache-policies/{entryName}", "observabilityGetCachePolicy", ObservabilityCachePoliciesTag),
        new("POST", "/api/v1/observability/cache-policies/{entryName}/invalidations", "observabilityInvalidateCachePolicy", ObservabilityCachePoliciesTag),
        new("GET", "/api/v1/observability/elasticsearch-log-pipeline/health", "observabilityGetElasticsearchLogPipelineHealth", ObservabilityElasticsearchLogPipelineTag),
        new("GET", "/api/v1/mqtt/status", "mqttGetBrokerStatus", MqttControlPlaneTag),
        new("GET", "/api/v1/mqtt/clients", "mqttListClients", MqttControlPlaneTag),
        new("GET", "/api/v1/mqtt/clients/{clientId}", "mqttGetClient", MqttControlPlaneTag),
        new("GET", "/api/v1/mqtt/messages", "mqttListMessages", MqttControlPlaneTag),
        new("GET", "/api/v1/mqtt/messages/{messageId}", "mqttGetMessage", MqttControlPlaneTag),
        new("POST", "/api/v1/mqtt/messages/publish", "mqttPublishMessage", MqttControlPlaneTag),
        new("GET", "/api/v1/cryptography/status", "cryptographyGetStatus", CryptographyGmKeysTag),
        new("GET", "/api/v1/cryptography/keys", "cryptographyListKeys", CryptographyGmKeysTag),
        new("GET", "/api/v1/cryptography/keys/{keyId}", "cryptographyGetKey", CryptographyGmKeysTag),
        new("POST", "/api/v1/cryptography/sm2/sign", "cryptographySm2Sign", CryptographyGmKeysTag),
        new("POST", "/api/v1/cryptography/sm2/verify", "cryptographySm2Verify", CryptographyGmKeysTag),
        new("GET", "/api/v1/document/host/categories", "documentHostListCategories", DocumentHostCategoriesTag),
        new("POST", "/api/v1/document/host/categories", "documentHostCreateCategory", DocumentHostCategoriesTag),
        new("PUT", "/api/v1/document/host/categories/{categoryId}", "documentHostUpdateCategory", DocumentHostCategoriesTag),
        new("POST", "/api/v1/document/host/categories/{categoryId}/delete", "documentHostDeleteCategory", DocumentHostCategoriesTag),
        new("GET", "/api/v1/document/host/items", "documentHostListItems", DocumentHostItemsTag),
        new("POST", "/api/v1/document/host/items", "documentHostCreateItem", DocumentHostItemsTag),
        new("PUT", "/api/v1/document/host/items/{itemId}", "documentHostUpdateItem", DocumentHostItemsTag),
        new("GET", "/api/v1/document/host/items/{itemId}/versions", "documentHostListItemVersions", DocumentHostItemsTag),
        new("POST", "/api/v1/document/host/items/{itemId}/versions", "documentHostAddItemVersion", DocumentHostItemsTag),
        new("POST", "/api/v1/document/host/items/{itemId}/versions/upload", "documentHostUploadItemVersion", DocumentHostItemsTag),
        new("GET", "/api/v1/document/host/items/{itemId}/content", "documentHostDownloadItemContent", DocumentHostItemsTag),
        new("GET", "/api/v1/document/host/items/{itemId}/preview", "documentHostPreviewItemContent", DocumentHostItemsTag),
        new("GET", "/api/v1/document/host/items/{itemId}/versions/{versionId}/preview", "documentHostPreviewItemVersionContent", DocumentHostItemsTag),
        new("POST", "/api/v1/document/host/items/{itemId}/delete", "documentHostDeleteItem", DocumentHostItemsTag),
        new("POST", "/api/v1/document/host/items/{itemId}/restore", "documentHostRestoreItem", DocumentHostItemsTag),
        new("POST", "/api/v1/document/host/items/{itemId}/versions/{versionId}/rollback", "documentHostRollbackItemVersion", DocumentHostItemsTag),
        new("GET", "/api/v1/document/host/tags", "documentHostListTags", DocumentHostTagsTag),
        new("POST", "/api/v1/document/host/tags", "documentHostCreateTag", DocumentHostTagsTag),
        new("PUT", "/api/v1/document/host/tags/{tagId}", "documentHostUpdateTag", DocumentHostTagsTag),
        new("POST", "/api/v1/document/host/tags/{tagId}/delete", "documentHostDeleteTag", DocumentHostTagsTag),
        new("GET", "/api/v1/document/host/permissions/by-document/{documentId}", "documentHostListDocumentPermissions", DocumentHostPermissionsTag),
        new("POST", "/api/v1/document/host/permissions", "documentHostSetDocumentPermissions", DocumentHostPermissionsTag),
        new("GET", "/api/v1/document/host/recycle-bin", "documentHostListRecycleBinItems", DocumentHostRecycleBinTag),
        new("POST", "/api/v1/document/host/recycle-bin/{id}/restore", "documentHostRestoreRecycleBinItem", DocumentHostRecycleBinTag),
        new("POST", "/api/v1/document/host/recycle-bin/{id}/purge", "documentHostPurgeRecycleBinItem", DocumentHostRecycleBinTag),
        new("GET", "/api/v1/document/host/shares", "documentHostListDocumentShares", DocumentHostSharesTag),
        new("POST", "/api/v1/document/host/shares", "documentHostCreateDocumentShare", DocumentHostSharesTag),
        new("POST", "/api/v1/document/host/shares/{id}/status", "documentHostUpdateDocumentShareStatus", DocumentHostSharesTag),
        new("POST", "/api/v1/document/public/shares/{shareCode}/access", "documentPublicAccessDocumentShare", DocumentPublicSharesTag),
        new("GET", "/api/v1/document/host/statistics", "documentHostGetDocumentStatistics", DocumentHostStatisticsTag),
    ];

    private sealed record ApprovedClientGenerationOperation(
        string Method,
        string Route,
        string OperationId,
        string PrimaryTag);
}
