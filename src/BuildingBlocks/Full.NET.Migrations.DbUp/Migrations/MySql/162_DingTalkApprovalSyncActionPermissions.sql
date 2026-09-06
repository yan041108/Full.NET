-- 162：为全部存量角色补齐钉钉审批镜像同步精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'notifications.dingtalk_approval_sync.read' AS PermissionCode
    UNION ALL SELECT 'notifications.dingtalk_approval_sync.create'
    UNION ALL SELECT 'notifications.dingtalk_approval_sync.retry'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
