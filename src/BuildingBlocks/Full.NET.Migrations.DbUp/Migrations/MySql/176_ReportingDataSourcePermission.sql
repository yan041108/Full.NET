-- 176：为全部存量角色补齐 Reporting 数据源精确动作权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'reporting.data_sources.read' AS PermissionCode
    UNION ALL SELECT 'reporting.data_sources.create'
    UNION ALL SELECT 'reporting.data_sources.update'
    UNION ALL SELECT 'reporting.data_sources.delete'
    UNION ALL SELECT 'reporting.data_sources.test'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
