-- 152：为全部存量角色补齐 OAuth 提供程序精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'identity.oauth_providers.read' AS PermissionCode
    UNION ALL SELECT 'identity.oauth_providers.create'
    UNION ALL SELECT 'identity.oauth_providers.update'
    UNION ALL SELECT 'identity.oauth_providers.delete'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
