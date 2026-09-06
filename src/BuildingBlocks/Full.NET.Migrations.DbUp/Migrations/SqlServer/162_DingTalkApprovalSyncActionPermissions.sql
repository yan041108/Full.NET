-- 162：为全部存量角色补齐钉钉审批镜像同步精确动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'notifications.dingtalk_approval_sync.read'),
        (N'notifications.dingtalk_approval_sync.create'),
        (N'notifications.dingtalk_approval_sync.retry')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
