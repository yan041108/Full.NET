-- 187：为全部存量角色补齐 AI 聊天权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'ai.chat.sessions.read' AS PermissionCode UNION ALL
    SELECT 'ai.chat.sessions.create' UNION ALL
    SELECT 'ai.chat.sessions.update' UNION ALL
    SELECT 'ai.chat.sessions.delete' UNION ALL
    SELECT 'ai.chat.messages.send' UNION ALL
    SELECT 'ai.chat.messages.cancel'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
