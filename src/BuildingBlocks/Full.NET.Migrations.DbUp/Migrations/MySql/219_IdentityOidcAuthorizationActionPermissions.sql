-- 219：为全部存量角色补齐 OIDC 授权授予 read/revoke 精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'identity.oidc_authorizations.read' AS PermissionCode
    UNION ALL SELECT 'identity.oidc_authorizations.revoke'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);