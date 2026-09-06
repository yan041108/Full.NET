-- 139：为全部存量角色补齐 OpenAccess 接入方应用五个精确动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'identity.open_access_clients.read'),
        (N'identity.open_access_clients.create'),
        (N'identity.open_access_clients.update'),
        (N'identity.open_access_clients.disable'),
        (N'identity.open_access_clients.rotate')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
