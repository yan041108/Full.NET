-- 160：为全部存量角色补齐国密控制面精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'cryptography.keys.read' AS PermissionCode
    UNION ALL SELECT 'cryptography.sm2.sign'
    UNION ALL SELECT 'cryptography.sm2.verify'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
