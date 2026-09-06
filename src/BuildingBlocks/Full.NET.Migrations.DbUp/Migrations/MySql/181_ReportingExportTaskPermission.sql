-- 181：为全部存量角色补齐 Reporting 导出任务权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'reporting.export_tasks.create' AS PermissionCode
    UNION ALL SELECT 'reporting.export_tasks.read'
    UNION ALL SELECT 'reporting.export_tasks.download'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
