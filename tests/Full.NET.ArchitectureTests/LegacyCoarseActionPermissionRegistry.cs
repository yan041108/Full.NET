using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 绮楃矑搴﹀鎿嶄綔 .write 鏉冮檺鍐荤粨娓呭崟锛涙柊澧?Endpoint 缁戝畾蹇呴』鍚屾鏇存柊娓呭崟涓庤矾绾垮浘搴撳瓨銆?/// </summary>
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
    };

    internal static bool IsCoarseWritePermission(string permissionCode) =>
        permissionCode.EndsWith(".write", StringComparison.Ordinal)
        && !RetiredPermissionCodes.Contains(permissionCode);

    internal static HashSet<string> AllowedBindings { get; } = new(StringComparer.Ordinal)
    {
        "POST /api/v1/code-generation/templates/{templateId:guid}/delete|codegen.templates.write",
        "POST /api/v1/code-generation/templates/|codegen.templates.write",
        "POST /api/v1/document/host/items/{itemId:guid}/versions|document.host_documents.write",
        "POST /api/v1/document/host/items/|document.host_documents.write",
        "POST /api/v1/files/host-files/{fileId:guid}/delete|files.files.write",
        "POST /api/v1/files/host-files/|files.files.write",
        "POST /api/v1/jobs/host-definitions/{definitionId:guid}/disable|jobs.definitions.write",
        "POST /api/v1/jobs/host-definitions/{definitionId:guid}/trigger|jobs.definitions.write",
        "POST /api/v1/jobs/host-definitions/|jobs.definitions.write",
        "POST /api/v1/jobs/host-schedules/{scheduleId:guid}/pause|jobs.schedules.write",
        "POST /api/v1/jobs/host-schedules/{scheduleId:guid}/resume|jobs.schedules.write",
        "POST /api/v1/jobs/host-schedules/|jobs.schedules.write",
        "POST /api/v1/notifications/host-announcements/{announcementId:guid}/publish|notifications.announcements.write",
        "POST /api/v1/notifications/host-announcements/|notifications.announcements.write",
        "POST /api/v1/notifications/host-inbox-messages/|notifications.inbox.write",
        "POST /api/v1/serial-numbers/rules/{ruleId:guid}/disable|serial_numbers.rules.write",
        "POST /api/v1/serial-numbers/rules/{ruleId:guid}/enable|serial_numbers.rules.write",
        "POST /api/v1/serial-numbers/rules/|serial_numbers.rules.write",
        "POST /api/v1/settings/config-entries/{configEntryId:guid}/disable|settings.config.write",
        "POST /api/v1/settings/config-entries/|settings.config.write",
        "POST /api/v1/settings/diagnostic-policy/restore|settings.diagnostic_policy.write",
        "POST /api/v1/settings/tenant-dict-items/{dictItemId:guid}/disable|settings.tenant_dict_types.write",
        "POST /api/v1/settings/tenant-dict-types/{dictTypeId:guid}/disable|settings.tenant_dict_types.write",
        "POST /api/v1/settings/tenant-dict-types/{dictTypeId:guid}/items/|settings.tenant_dict_types.write",
        "POST /api/v1/settings/tenant-dict-types/|settings.tenant_dict_types.write",
        "PUT /api/v1/code-generation/templates/{templateId:guid}|codegen.templates.write",
        "PUT /api/v1/document/host/items/{itemId:guid}|document.host_documents.write",
        "PUT /api/v1/jobs/host-definitions/{definitionId:guid}|jobs.definitions.write",
        "PUT /api/v1/jobs/host-schedules/{scheduleId:guid}|jobs.schedules.write",
        "PUT /api/v1/notifications/host-announcements/{announcementId:guid}|notifications.announcements.write",
        "PUT /api/v1/serial-numbers/rules/{ruleId:guid}|serial_numbers.rules.write",
        "PUT /api/v1/settings/config-entries/{configEntryId:guid}|settings.config.write",
        "PUT /api/v1/settings/diagnostic-policy/|settings.diagnostic_policy.write",
        "PUT /api/v1/settings/tenant-dict-items/{dictItemId:guid}|settings.tenant_dict_types.write",
        "PUT /api/v1/settings/tenant-dict-types/{dictTypeId:guid}|settings.tenant_dict_types.write",
    };
}