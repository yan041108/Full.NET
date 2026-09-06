-- 158：为全部存量角色补齐 MQTT 控制面精确动作权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'mqtt.broker.read' AS PermissionCode
    UNION ALL SELECT 'mqtt.clients.read'
    UNION ALL SELECT 'mqtt.messages.read'
    UNION ALL SELECT 'mqtt.messages.publish'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
