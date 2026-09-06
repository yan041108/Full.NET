-- 164：为全部存量角色补齐微信小程序绑定与订阅动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'notifications.wechat_miniprogram_bindings.read' AS PermissionCode
    UNION ALL SELECT 'notifications.wechat_miniprogram_bindings.bind'
    UNION ALL SELECT 'notifications.wechat_miniprogram_bindings.record_subscription'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
