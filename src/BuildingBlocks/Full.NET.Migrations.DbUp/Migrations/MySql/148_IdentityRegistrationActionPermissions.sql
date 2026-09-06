-- 148：为全部存量角色补齐注册策略与注册方式精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'identity.registration_policy.read' AS PermissionCode
    UNION ALL SELECT 'identity.registration_policy.update'
    UNION ALL SELECT 'identity.registration_ways.read'
    UNION ALL SELECT 'identity.registration_ways.create'
    UNION ALL SELECT 'identity.registration_ways.update'
    UNION ALL SELECT 'identity.registration_ways.delete'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
