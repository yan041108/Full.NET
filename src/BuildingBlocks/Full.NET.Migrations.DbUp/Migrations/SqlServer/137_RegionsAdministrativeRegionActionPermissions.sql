-- 137：为全部存量角色补齐行政区域五个精确动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'regions.administrative_regions.read'),
        (N'regions.administrative_regions.create'),
        (N'regions.administrative_regions.update'),
        (N'regions.administrative_regions.delete'),
        (N'regions.administrative_regions.import')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
