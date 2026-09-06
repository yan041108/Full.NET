-- 148：为全部存量角色补齐注册策略与注册方式精确动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'identity.registration_policy.read'),
        (N'identity.registration_policy.update'),
        (N'identity.registration_ways.read'),
        (N'identity.registration_ways.create'),
        (N'identity.registration_ways.update'),
        (N'identity.registration_ways.delete')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
