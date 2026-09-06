-- 196：为全部存量角色补齐 GoView 大屏项目权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'goview.projects.read'),
        (N'goview.projects.create'),
        (N'goview.projects.update'),
        (N'goview.projects.publish'),
        (N'goview.projects.preview')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
