-- 143：为全部存量角色补齐 Host 公告收件与阅读统计精确动作权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'notifications.announcements.received.read'),
        (N'notifications.announcements.received.mark_read'),
        (N'notifications.announcements.received.mark_all_read'),
        (N'notifications.announcements.read_stats')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
