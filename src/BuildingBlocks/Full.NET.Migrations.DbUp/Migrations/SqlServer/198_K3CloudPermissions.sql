-- 198：为全部存量角色补齐 K3Cloud 连接与单据同步权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'k3cloud.connections.read'),
        (N'k3cloud.connections.create'),
        (N'k3cloud.connections.update'),
        (N'k3cloud.connections.test'),
        (N'k3cloud.document_syncs.read'),
        (N'k3cloud.document_syncs.create'),
        (N'k3cloud.document_syncs.retry')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
