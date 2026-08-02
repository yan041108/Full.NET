using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 粗粒度多操作 .write 权限冻结清单；新增 Endpoint 绑定必须同步更新清单与路线图库存。
/// </summary>
internal static class LegacyCoarseActionPermissionRegistry
{
    private static readonly HashSet<string> RetiredPermissionCodes = new(StringComparer.Ordinal)
    {
        IdentityUserManagementPermissions.Write,
        IdentityRoleManagementPermissions.Write,
        IdentityRoleFieldGrantPermissions.Write,
        IdentityMenuManagementPermissions.Write,
    };

    internal static bool IsCoarseWritePermission(string permissionCode) =>
        permissionCode.EndsWith(".write", StringComparison.Ordinal)
        && !RetiredPermissionCodes.Contains(permissionCode);

    internal static HashSet<string> AllowedBindings { get; } = new(StringComparer.Ordinal)
    {
        "GET /api/v1/organization/user-positions/assignable-users|organization.user_positions.write",
        "GET /api/v1/organization/user-units/assignable-users|organization.user_units.write",
        "POST /api/v1/code-generation/templates/{templateId:guid}/delete|codegen.templates.write",
        "POST /api/v1/code-generation/templates/|codegen.templates.write",
        "POST /api/v1/document/host/items/{itemId:guid}/versions|document.host_documents.write",
        "POST /api/v1/document/host/items/|document.host_documents.write",
        "POST /api/v1/files/host-files/{fileId:guid}/delete|files.files.write",
        "POST /api/v1/files/host-files/|files.files.write",
        "POST /api/v1/identity/api-keys/{apiKeyId:guid}/disable|identity.api_keys.write",
        "POST /api/v1/identity/api-keys/{apiKeyId:guid}/rotate|identity.api_keys.write",
        "POST /api/v1/identity/api-keys/|identity.api_keys.write",
        "POST /api/v1/identity/online-sessions/{sessionId:guid}/revoke|identity.sessions.write",
        "POST /api/v1/jobs/host-definitions/{definitionId:guid}/disable|jobs.definitions.write",
        "POST /api/v1/jobs/host-definitions/{definitionId:guid}/trigger|jobs.definitions.write",
        "POST /api/v1/jobs/host-definitions/|jobs.definitions.write",
        "POST /api/v1/jobs/host-schedules/{scheduleId:guid}/pause|jobs.schedules.write",
        "POST /api/v1/jobs/host-schedules/{scheduleId:guid}/resume|jobs.schedules.write",
        "POST /api/v1/jobs/host-schedules/|jobs.schedules.write",
        "POST /api/v1/notifications/host-announcements/{announcementId:guid}/publish|notifications.announcements.write",
        "POST /api/v1/notifications/host-announcements/|notifications.announcements.write",
        "POST /api/v1/notifications/host-inbox-messages/|notifications.inbox.write",
        "POST /api/v1/organization/position-levels/{positionLevelId:guid}/disable|organization.position_levels.write",
        "POST /api/v1/organization/position-levels/|organization.position_levels.write",
        "POST /api/v1/organization/positions/{positionId:guid}/disable|organization.positions.write",
        "POST /api/v1/organization/positions/|organization.positions.write",
        "POST /api/v1/organization/units/{unitId:guid}/disable|organization.units.write",
        "POST /api/v1/organization/units/|organization.units.write",
        "POST /api/v1/organization/user-positions/{assignmentId:guid}/disable|organization.user_positions.write",
        "POST /api/v1/organization/user-positions/|organization.user_positions.write",
        "POST /api/v1/organization/user-units/{assignmentId:guid}/disable|organization.user_units.write",
        "POST /api/v1/organization/user-units/|organization.user_units.write",
        "POST /api/v1/serial-numbers/rules/{ruleId:guid}/disable|serial_numbers.rules.write",
        "POST /api/v1/serial-numbers/rules/{ruleId:guid}/enable|serial_numbers.rules.write",
        "POST /api/v1/serial-numbers/rules/|serial_numbers.rules.write",
        "POST /api/v1/settings/config-entries/{configEntryId:guid}/disable|settings.config.write",
        "POST /api/v1/settings/config-entries/|settings.config.write",
        "POST /api/v1/settings/diagnostic-policy/restore|settings.diagnostic_policy.write",
        "POST /api/v1/settings/dict-items/{dictItemId:guid}/disable|settings.dict_types.write",
        "POST /api/v1/settings/dict-types/{dictTypeId:guid}/disable|settings.dict_types.write",
        "POST /api/v1/settings/dict-types/{dictTypeId:guid}/items/|settings.dict_types.write",
        "POST /api/v1/settings/dict-types/|settings.dict_types.write",
        "POST /api/v1/settings/tenant-dict-items/{dictItemId:guid}/disable|settings.tenant_dict_types.write",
        "POST /api/v1/settings/tenant-dict-types/{dictTypeId:guid}/disable|settings.tenant_dict_types.write",
        "POST /api/v1/settings/tenant-dict-types/{dictTypeId:guid}/items/|settings.tenant_dict_types.write",
        "POST /api/v1/settings/tenant-dict-types/|settings.tenant_dict_types.write",
        "POST /api/v1/tenancy/tenant-packages/{packageId:guid}/disable|tenancy.tenant_packages.write",
        "POST /api/v1/tenancy/tenant-packages/|tenancy.tenant_packages.write",
        "POST /api/v1/tenancy/tenants/{tenantId:guid}/disable|tenancy.tenants.write",
        "POST /api/v1/tenancy/tenants/{tenantId:guid}/package|tenancy.tenants.write",
        "POST /api/v1/tenancy/tenants/|tenancy.tenants.write",
        "PUT /api/v1/code-generation/templates/{templateId:guid}|codegen.templates.write",
        "PUT /api/v1/document/host/items/{itemId:guid}|document.host_documents.write",
        "PUT /api/v1/jobs/host-definitions/{definitionId:guid}|jobs.definitions.write",
        "PUT /api/v1/jobs/host-schedules/{scheduleId:guid}|jobs.schedules.write",
        "PUT /api/v1/notifications/host-announcements/{announcementId:guid}|notifications.announcements.write",
        "PUT /api/v1/organization/position-levels/{positionLevelId:guid}|organization.position_levels.write",
        "PUT /api/v1/organization/positions/{positionId:guid}/position-level|organization.positions.write",
        "PUT /api/v1/organization/positions/{positionId:guid}/unit|organization.positions.write",
        "PUT /api/v1/organization/positions/{positionId:guid}|organization.positions.write",
        "PUT /api/v1/organization/units/{unitId:guid}|organization.units.write",
        "PUT /api/v1/organization/user-positions/{assignmentId:guid}|organization.user_positions.write",
        "PUT /api/v1/organization/user-units/{assignmentId:guid}|organization.user_units.write",
        "PUT /api/v1/serial-numbers/rules/{ruleId:guid}|serial_numbers.rules.write",
        "PUT /api/v1/settings/config-entries/{configEntryId:guid}|settings.config.write",
        "PUT /api/v1/settings/diagnostic-policy/|settings.diagnostic_policy.write",
        "PUT /api/v1/settings/dict-items/{dictItemId:guid}|settings.dict_types.write",
        "PUT /api/v1/settings/dict-types/{dictTypeId:guid}|settings.dict_types.write",
        "PUT /api/v1/settings/tenant-dict-items/{dictItemId:guid}|settings.tenant_dict_types.write",
        "PUT /api/v1/settings/tenant-dict-types/{dictTypeId:guid}|settings.tenant_dict_types.write",
        "PUT /api/v1/tenancy/tenant-packages/{packageId:guid}|tenancy.tenant_packages.write",
        "PUT /api/v1/tenancy/tenants/{tenantId:guid}|tenancy.tenants.write",
    };
}
