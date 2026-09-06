-- 133：为全部存量角色补齐个人日程五个精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'calendar.personal_schedules.read' AS PermissionCode
    UNION ALL SELECT 'calendar.personal_schedules.create'
    UNION ALL SELECT 'calendar.personal_schedules.update'
    UNION ALL SELECT 'calendar.personal_schedules.delete'
    UNION ALL SELECT 'calendar.personal_schedules.set_status'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
