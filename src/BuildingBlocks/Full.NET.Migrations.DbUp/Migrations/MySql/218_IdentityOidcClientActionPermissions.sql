-- 218：为全部存量角色补齐 OIDC 客户端五个精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'identity.oidc_clients.read' AS PermissionCode
    UNION ALL SELECT 'identity.oidc_clients.create'
    UNION ALL SELECT 'identity.oidc_clients.update'
    UNION ALL SELECT 'identity.oidc_clients.disable'
    UNION ALL SELECT 'identity.oidc_clients.rotate'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);