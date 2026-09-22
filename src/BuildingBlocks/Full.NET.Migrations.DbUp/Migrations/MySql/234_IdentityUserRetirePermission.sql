-- 234：为已具备 identity.users.disable 的角色幂等授予 identity.users.retire。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT disableRole.RoleId, 'identity.users.retire'
FROM fn_identity_role_permission AS disableRole
WHERE disableRole.PermissionCode = 'identity.users.disable'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = disableRole.RoleId
      AND existing.PermissionCode = 'identity.users.retire'
  );
