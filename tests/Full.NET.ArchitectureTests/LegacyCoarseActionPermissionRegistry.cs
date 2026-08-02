using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 缁鐭戞惔锕€顦块幙宥勭稊 .write 閺夊啴妾洪崘鑽ょ波濞撳懎宕熼敍娑欐煀婢?Endpoint 缂佹垵鐣捐箛鍛淬€忛崥灞绢劄閺囧瓨鏌婂〒鍛礋娑撳氦鐭剧痪鍨禈鎼存挸鐡ㄩ妴?/// </summary>
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
    };

    internal static bool IsCoarseWritePermission(string permissionCode) =>
        permissionCode.EndsWith(".write", StringComparison.Ordinal)
        && !RetiredPermissionCodes.Contains(permissionCode);

    internal static HashSet<string> AllowedBindings { get; } = new(StringComparer.Ordinal);
}
