-- 150：为全部存量角色补齐 LDAP 连接精确动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'identity.ldap_connections.read'),
        (N'identity.ldap_connections.create'),
        (N'identity.ldap_connections.update'),
        (N'identity.ldap_connections.delete'),
        (N'identity.ldap_connections.test'),
        (N'identity.ldap_connections.preview_sync')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
