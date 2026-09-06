-- 178：为全部存量角色补齐 Reporting 分组/定义/Query Port 权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'reporting.groups.read'),
        (N'reporting.groups.create'),
        (N'reporting.groups.update'),
        (N'reporting.groups.delete'),
        (N'reporting.definitions.read'),
        (N'reporting.definitions.create'),
        (N'reporting.definitions.update'),
        (N'reporting.definitions.delete'),
        (N'reporting.definitions.publish'),
        (N'reporting.query_ports.read')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
