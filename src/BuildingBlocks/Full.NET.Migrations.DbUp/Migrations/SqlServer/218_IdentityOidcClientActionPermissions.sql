-- 218：为全部存量角色补齐 OIDC 客户端五个精确动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'identity.oidc_clients.read'),
        (N'identity.oidc_clients.create'),
        (N'identity.oidc_clients.update'),
        (N'identity.oidc_clients.disable'),
        (N'identity.oidc_clients.rotate')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);