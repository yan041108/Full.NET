-- 176：为全部存量角色补齐 Reporting 数据源精确动作权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'reporting.data_sources.read'),
        (N'reporting.data_sources.create'),
        (N'reporting.data_sources.update'),
        (N'reporting.data_sources.delete'),
        (N'reporting.data_sources.test')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
