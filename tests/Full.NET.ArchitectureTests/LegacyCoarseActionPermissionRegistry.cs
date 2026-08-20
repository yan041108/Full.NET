using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 冻结粗粒度动作权限边界：退役码不可再绑定 Endpoint；未退役的 `.write`/`.manage` 等多动作权限只能出现在有限 allowlist 中。
/// </summary>
internal static class LegacyCoarseActionPermissionRegistry
{
    private static readonly HashSet<string> RetiredPermissionCodes = new(StringComparer.Ordinal)
    {
        IdentityUserManagementPermissions.Write,
        IdentityRoleManagementPermissions.Write,
        IdentityRoleFieldGrantPermissions.Write,
        IdentityMenuManagementPermissions.Write,
        IdentitySessionManagementPermissions.Write,
        IdentityApiKeyManagementPermissions.Write,
        TenancyTenantManagementPermissions.Write,
        TenancyTenantPackagePermissions.Write,
        OrganizationUnitManagementPermissions.Write,
        OrganizationPositionManagementPermissions.Write,
        OrganizationPositionLevelManagementPermissions.Write,
        OrganizationUserPositionManagementPermissions.Write,
        OrganizationUserUnitManagementPermissions.Write,
        DictTypeManagementPermissions.Write,
        TenantDictTypeManagementPermissions.Write,
        ConfigEntryManagementPermissions.Write,
        DiagnosticPolicyManagementPermissions.Write,
        "files.files.write",
        "notifications.announcements.write",
        "notifications.inbox.write",
        "jobs.definitions.write",
        "jobs.schedules.write",
        "codegen.templates.write",
        "serial_numbers.rules.write",
        "document.host_documents.write",
        "document.categories.manage",
        "document.tags.manage",
        "identity.super_administrators.manage",
    };

    internal static bool IsRetiredPermissionCode(string permissionCode) =>
        RetiredPermissionCodes.Contains(permissionCode);

    internal static bool IsCoarseManagePermission(string permissionCode) =>
        permissionCode.EndsWith(".manage", StringComparison.Ordinal);

    internal static bool IsCoarseWritePermission(string permissionCode) =>
        permissionCode.EndsWith(".write", StringComparison.Ordinal)
        && !RetiredPermissionCodes.Contains(permissionCode);

    internal static bool IsCoarseActionPermission(string permissionCode) =>
        IsCoarseWritePermission(permissionCode)
        || IsCoarseManagePermission(permissionCode);

    internal static HashSet<string> AllowedBindings { get; } = new(StringComparer.Ordinal)
    {
    };
}
