-- 230：为已具备 Host 维护能力的角色补齐管理页导航权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT DISTINCT existing.RoleId, 'platform.host_release_notes.read'
FROM fn_identity_role_permission AS existing
WHERE existing.PermissionCode = 'platform.release_notes.create'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS granted
    WHERE granted.RoleId = existing.RoleId
      AND granted.PermissionCode = 'platform.host_release_notes.read'
);

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT DISTINCT existing.RoleId, 'regions.administrative_regions.manage'
FROM fn_identity_role_permission AS existing
WHERE existing.PermissionCode = 'regions.administrative_regions.create'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS granted
    WHERE granted.RoleId = existing.RoleId
      AND granted.PermissionCode = 'regions.administrative_regions.manage'
);
