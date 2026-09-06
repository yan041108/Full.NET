-- 183：为全部存量角色补齐 Printing 模板与表单 Schema 权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'printing.templates.read'),
        (N'printing.templates.create'),
        (N'printing.templates.update'),
        (N'printing.templates.publish'),
        (N'printing.templates.preview'),
        (N'printing.form_schemas.read')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
