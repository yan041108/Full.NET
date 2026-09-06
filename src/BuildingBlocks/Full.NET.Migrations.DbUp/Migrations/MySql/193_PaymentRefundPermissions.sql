-- 193：为全部存量角色补齐支付对账、退款权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'payments.orders.reconcile' AS PermissionCode
    UNION ALL SELECT 'payments.refunds.read'
    UNION ALL SELECT 'payments.refunds.create'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
