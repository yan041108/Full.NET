using System.Text.RegularExpressions;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Hosting.Observability;
using Full.NET.Modularity.Modules;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MessagePack;
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
    private const string FilesTag = "FilesHostFiles";
    private const string SettingsTag = "SettingsHostConfigEntries";

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
        builder.Services.AddFullNetMessagePack();
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
        new("POST", "/api/v1/identity/users/import", "identityImportHostUsers", IdentityHostUsersTag),
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
    ];

    private sealed record ApprovedClientGenerationOperation(
        string Method,
        string Route,
        string OperationId,
        string PrimaryTag);
}
