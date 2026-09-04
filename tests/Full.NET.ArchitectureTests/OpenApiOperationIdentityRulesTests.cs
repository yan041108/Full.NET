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
    private const string NotificationsHostAnnouncementsTag = "NotificationsHostAnnouncements";
    private const string NotificationsMyInboxMessagesTag = "NotificationsMyInboxMessages";
    private const string NotificationsHostInboxMessagesTag = "NotificationsHostInboxMessages";
    private const string CodeGenerationPreviewsTag = "CodeGenerationPreviews";
    private const string CodeGenerationRunsTag = "CodeGenerationRuns";
    private const string CodeGenerationTemplatesTag = "CodeGenerationTemplates";
    private const string CodeGenerationCatalogTag = "CodeGenerationCatalog";
    private const string SerialNumbersHostRulesTag = "SerialNumbersHostRules";
    private const string ObservabilityLogFilesTag = "ObservabilityLogFiles";
    private const string DocumentHostCategoriesTag = "DocumentHostCategories";
    private const string DocumentHostItemsTag = "DocumentHostItems";
    private const string DocumentHostTagsTag = "DocumentHostTags";
    private const string DocumentHostPermissionsTag = "DocumentHostPermissions";
    private const string DocumentHostRecycleBinTag = "DocumentHostRecycleBin";
    private const string DocumentHostSharesTag = "DocumentHostShares";
    private const string DocumentPublicSharesTag = "DocumentPublicShares";
    private const string DocumentHostStatisticsTag = "DocumentHostStatistics";

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
        new("GET", "/api/v1/identity/users/{userId}/roles", "identityGetHostUserRoles", IdentityHostUsersTag),
        new("PUT", "/api/v1/identity/users/{userId}/roles", "identityReplaceHostUserRoles", IdentityHostUsersTag),
        new("GET", "/api/v1/identity/authorization-tree", "identityGetAuthorizationTree", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/field-projections/catalog", "identityListFieldProjectionCatalog", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/roles", "identityListHostRoles", IdentityHostRolesTag),
        new("POST", "/api/v1/identity/roles", "identityCreateHostRole", IdentityHostRolesTag),
        new("GET", "/api/v1/identity/roles/{roleId}", "identityGetHostRole", IdentityHostRolesTag),
        new("PUT", "/api/v1/identity/roles/{roleId}", "identityUpdateHostRole", IdentityHostRolesTag),
        new("PUT", "/api/v1/identity/roles/{roleId}/permissions", "identityReplaceHostRolePermissions", IdentityHostRolesTag),
        new("POST", "/api/v1/identity/roles/{roleId}/disable", "identityDisableHostRole", IdentityHostRolesTag),
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
        new("GET", "/api/v1/identity/online-sessions", "identityListHostOnlineSessions", IdentityHostOnlineSessionsTag),
        new("POST", "/api/v1/identity/online-sessions/{sessionId}/revoke", "identityRevokeHostOnlineSession", IdentityHostOnlineSessionsTag),
        new("GET", "/api/v1/identity/modules", "identityListHostModules", IdentityHostModulesTag),
        new("GET", "/api/v1/identity/modules/{moduleKey}", "identityGetHostModule", IdentityHostModulesTag),
        new("GET", "/api/v1/me", "identityGetCurrentUser", IdentityMeTag),
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
        new("POST", "/api/v1/tenancy/tenants/{tenantId}/package", "tenancyAssignHostTenantPackage", TenancyHostTenantsTag),
        new("GET", "/api/v1/tenancy/tenant-packages", "tenancyListHostTenantPackages", TenancyHostTenantPackagesTag),
        new("GET", "/api/v1/tenancy/tenant-packages/{packageId}", "tenancyGetHostTenantPackage", TenancyHostTenantPackagesTag),
        new("POST", "/api/v1/tenancy/tenant-packages", "tenancyCreateHostTenantPackage", TenancyHostTenantPackagesTag),
        new("PUT", "/api/v1/tenancy/tenant-packages/{packageId}", "tenancyUpdateHostTenantPackage", TenancyHostTenantPackagesTag),
        new("POST", "/api/v1/tenancy/tenant-packages/{packageId}/disable", "tenancyDisableHostTenantPackage", TenancyHostTenantPackagesTag),
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
        new("GET", "/api/v1/files/host-files/{fileId}/content", "filesDownloadHostFileContent", FilesTag),
        new("POST", "/api/v1/files/host-files/{fileId}/delete", "filesDeleteHostFile", FilesTag),
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
        new("GET", "/api/v1/auditing/operation-logs", "auditingListHostOperationLogs", AuditingHostOperationLogsTag),
        new("GET", "/api/v1/auditing/exception-logs", "auditingListHostExceptionLogs", AuditingHostExceptionLogsTag),
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
        new("POST", "/api/v1/jobs/host-executions/clear", "jobsClearHostJobExecutions", JobsHostJobExecutionsTag),
        new("GET", "/api/v1/jobs/host-schedules", "jobsListHostJobSchedules", JobsHostJobSchedulesTag),
        new("GET", "/api/v1/jobs/host-schedules/definition-options", "jobsListHostJobScheduleDefinitionOptions", JobsHostJobSchedulesTag),
        new("GET", "/api/v1/jobs/host-schedules/cron-preview", "jobsPreviewHostJobScheduleCron", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules", "jobsCreateHostJobSchedule", JobsHostJobSchedulesTag),
        new("PUT", "/api/v1/jobs/host-schedules/{scheduleId}", "jobsUpdateHostJobSchedule", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules/{scheduleId}/pause", "jobsPauseHostJobSchedule", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules/{scheduleId}/resume", "jobsResumeHostJobSchedule", JobsHostJobSchedulesTag),
        new("POST", "/api/v1/jobs/host-schedules/{scheduleId}/delete", "jobsDeleteHostJobSchedule", JobsHostJobSchedulesTag),
        new("GET", "/api/v1/jobs/host-health", "jobsGetHostJobHealth", JobsHostJobHealthTag),
        new("GET", "/api/v1/notifications/host-announcements", "notificationsListHostAnnouncements", NotificationsHostAnnouncementsTag),
        new("POST", "/api/v1/notifications/host-announcements", "notificationsCreateHostAnnouncement", NotificationsHostAnnouncementsTag),
        new("PUT", "/api/v1/notifications/host-announcements/{announcementId}", "notificationsUpdateHostAnnouncement", NotificationsHostAnnouncementsTag),
        new("POST", "/api/v1/notifications/host-announcements/{announcementId}/publish", "notificationsPublishHostAnnouncement", NotificationsHostAnnouncementsTag),
        new("POST", "/api/v1/notifications/host-announcements/{announcementId}/retract", "notificationsRetractHostAnnouncement", NotificationsHostAnnouncementsTag),
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
        new("GET", "/api/v1/code-generation/catalog/tables/{tableName}/columns", "codeGenerationListCatalogColumns", CodeGenerationCatalogTag),
        new("POST", "/api/v1/code-generation/catalog/column-sync", "codeGenerationSyncCatalogColumns", CodeGenerationCatalogTag),
        new("GET", "/api/v1/serial-numbers/rules", "serialNumbersListRules", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules", "serialNumbersCreateRule", SerialNumbersHostRulesTag),
        new("PUT", "/api/v1/serial-numbers/rules/{ruleId}", "serialNumbersUpdateRule", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/{ruleId}/enable", "serialNumbersEnableRule", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/{ruleId}/disable", "serialNumbersDisableRule", SerialNumbersHostRulesTag),
        new("POST", "/api/v1/serial-numbers/rules/preview", "serialNumbersPreviewSerialNumber", SerialNumbersHostRulesTag),
        new("GET", "/api/v1/observability/log-files", "observabilityListLogFiles", ObservabilityLogFilesTag),
        new("GET", "/api/v1/observability/log-files/{id}/tail", "observabilityTailLogFile", ObservabilityLogFilesTag),
        new("GET", "/api/v1/observability/log-files/{id}/download", "observabilityDownloadLogFile", ObservabilityLogFilesTag),
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
