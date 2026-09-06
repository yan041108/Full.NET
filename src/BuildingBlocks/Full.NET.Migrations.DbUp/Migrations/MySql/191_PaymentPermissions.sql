-- 191：为全部存量角色补齐支付商户配置与支付订单权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'payments.merchants.read' AS PermissionCode
    UNION ALL SELECT 'payments.merchants.create'
    UNION ALL SELECT 'payments.merchants.update'
    UNION ALL SELECT 'payments.orders.read'
    UNION ALL SELECT 'payments.orders.create'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
