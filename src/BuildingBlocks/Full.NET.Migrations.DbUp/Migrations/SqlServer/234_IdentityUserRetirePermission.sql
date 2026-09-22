-- 234：为已具备 identity.users.disable 的角色幂等授予 identity.users.retire。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT disableRole.RoleId, N'identity.users.retire'
FROM dbo.fn_identity_role_permission AS disableRole
WHERE disableRole.PermissionCode = N'identity.users.disable'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = disableRole.RoleId
      AND existing.PermissionCode = N'identity.users.retire'
  );
