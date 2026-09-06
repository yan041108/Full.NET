-- 196：为全部存量角色补齐 GoView 大屏项目权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'goview.projects.read' AS PermissionCode
    UNION ALL SELECT 'goview.projects.create'
    UNION ALL SELECT 'goview.projects.update'
    UNION ALL SELECT 'goview.projects.publish'
    UNION ALL SELECT 'goview.projects.preview'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
