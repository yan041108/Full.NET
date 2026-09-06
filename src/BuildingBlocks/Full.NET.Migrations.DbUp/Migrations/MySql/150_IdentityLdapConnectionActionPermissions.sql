-- 150：为全部存量角色补齐 LDAP 连接精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'identity.ldap_connections.read' AS PermissionCode
    UNION ALL SELECT 'identity.ldap_connections.create'
    UNION ALL SELECT 'identity.ldap_connections.update'
    UNION ALL SELECT 'identity.ldap_connections.delete'
    UNION ALL SELECT 'identity.ldap_connections.test'
    UNION ALL SELECT 'identity.ldap_connections.preview_sync'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
