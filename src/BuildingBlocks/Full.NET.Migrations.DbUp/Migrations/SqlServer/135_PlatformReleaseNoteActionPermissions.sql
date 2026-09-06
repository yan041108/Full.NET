-- 135：为全部存量角色补齐更新日志七个精确动作权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'platform.release_notes.read'),
        (N'platform.release_notes.create'),
        (N'platform.release_notes.update'),
        (N'platform.release_notes.publish'),
        (N'platform.release_notes.retract'),
        (N'platform.release_notes.delete'),
        (N'platform.release_notes.mark_read')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
