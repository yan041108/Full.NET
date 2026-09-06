-- 178：为全部存量角色补齐 Reporting 分组/定义/Query Port 权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'reporting.groups.read' AS PermissionCode
    UNION ALL SELECT 'reporting.groups.create'
    UNION ALL SELECT 'reporting.groups.update'
    UNION ALL SELECT 'reporting.groups.delete'
    UNION ALL SELECT 'reporting.definitions.read'
    UNION ALL SELECT 'reporting.definitions.create'
    UNION ALL SELECT 'reporting.definitions.update'
    UNION ALL SELECT 'reporting.definitions.delete'
    UNION ALL SELECT 'reporting.definitions.publish'
    UNION ALL SELECT 'reporting.query_ports.read'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
