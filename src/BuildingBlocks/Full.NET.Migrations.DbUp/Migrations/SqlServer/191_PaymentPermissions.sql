-- 191：为全部存量角色补齐支付商户配置与支付订单权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'payments.merchants.read'),
        (N'payments.merchants.create'),
        (N'payments.merchants.update'),
        (N'payments.orders.read'),
        (N'payments.orders.create')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
