-- 137：为全部存量角色补齐行政区域五个精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'regions.administrative_regions.read' AS PermissionCode
    UNION ALL SELECT 'regions.administrative_regions.create'
    UNION ALL SELECT 'regions.administrative_regions.update'
    UNION ALL SELECT 'regions.administrative_regions.delete'
    UNION ALL SELECT 'regions.administrative_regions.import'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
