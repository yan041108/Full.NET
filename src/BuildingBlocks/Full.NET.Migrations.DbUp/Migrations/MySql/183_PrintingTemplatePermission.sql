-- 183：为全部存量角色补齐 Printing 模板与表单 Schema 权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'printing.templates.read' AS PermissionCode
    UNION ALL SELECT 'printing.templates.create'
    UNION ALL SELECT 'printing.templates.update'
    UNION ALL SELECT 'printing.templates.publish'
    UNION ALL SELECT 'printing.templates.preview'
    UNION ALL SELECT 'printing.form_schemas.read'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
