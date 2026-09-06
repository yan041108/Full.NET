-- 155：为全部存量角色补齐授权备份执行器精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'platform.backup_tasks.read' AS PermissionCode
    UNION ALL SELECT 'platform.backup_runs.read'
    UNION ALL SELECT 'platform.backup_runs.download'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
