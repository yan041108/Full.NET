-- 185：为全部存量角色补齐 AI 模型配置与租户配额权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'ai.models.read'),
        (N'ai.models.create'),
        (N'ai.models.update'),
        (N'ai.models.test'),
        (N'ai.quotas.read'),
        (N'ai.quotas.update')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
