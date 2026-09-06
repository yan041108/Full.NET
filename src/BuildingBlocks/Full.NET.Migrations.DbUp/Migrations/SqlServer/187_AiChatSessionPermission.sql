-- 187：为全部存量角色补齐 AI 聊天权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'ai.chat.sessions.read'),
        (N'ai.chat.sessions.create'),
        (N'ai.chat.sessions.update'),
        (N'ai.chat.sessions.delete'),
        (N'ai.chat.messages.send'),
        (N'ai.chat.messages.cancel')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
