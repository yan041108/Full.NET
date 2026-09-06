-- 143：为全部存量角色补齐 Host 公告收件与阅读统计精确动作权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'notifications.announcements.received.read' AS PermissionCode
    UNION ALL SELECT 'notifications.announcements.received.mark_read'
    UNION ALL SELECT 'notifications.announcements.received.mark_all_read'
    UNION ALL SELECT 'notifications.announcements.read_stats'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
