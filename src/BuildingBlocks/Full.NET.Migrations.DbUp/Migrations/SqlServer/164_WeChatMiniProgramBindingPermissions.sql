-- 164：为全部存量角色补齐微信小程序绑定与订阅动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'notifications.wechat_miniprogram_bindings.read'),
        (N'notifications.wechat_miniprogram_bindings.bind'),
        (N'notifications.wechat_miniprogram_bindings.record_subscription')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
