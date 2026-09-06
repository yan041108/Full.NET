-- 198：为全部存量角色补齐 K3Cloud 连接与单据同步权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'k3cloud.connections.read' AS PermissionCode
    UNION ALL SELECT 'k3cloud.connections.create'
    UNION ALL SELECT 'k3cloud.connections.update'
    UNION ALL SELECT 'k3cloud.connections.test'
    UNION ALL SELECT 'k3cloud.document_syncs.read'
    UNION ALL SELECT 'k3cloud.document_syncs.create'
    UNION ALL SELECT 'k3cloud.document_syncs.retry'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
