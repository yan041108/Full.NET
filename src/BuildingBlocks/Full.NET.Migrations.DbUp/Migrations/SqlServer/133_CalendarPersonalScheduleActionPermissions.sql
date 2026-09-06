-- 133：为全部存量角色补齐个人日程五个精确动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'calendar.personal_schedules.read'),
        (N'calendar.personal_schedules.create'),
        (N'calendar.personal_schedules.update'),
        (N'calendar.personal_schedules.delete'),
        (N'calendar.personal_schedules.set_status')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
