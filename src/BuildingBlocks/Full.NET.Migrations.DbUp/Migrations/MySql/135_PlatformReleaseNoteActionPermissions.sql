-- 135：为全部存量角色补齐更新日志七个精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'platform.release_notes.read' AS PermissionCode
    UNION ALL SELECT 'platform.release_notes.create'
    UNION ALL SELECT 'platform.release_notes.update'
    UNION ALL SELECT 'platform.release_notes.publish'
    UNION ALL SELECT 'platform.release_notes.retract'
    UNION ALL SELECT 'platform.release_notes.delete'
    UNION ALL SELECT 'platform.release_notes.mark_read'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
