-- 230：为已具备 Host 维护能力的角色补齐管理页导航权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT DISTINCT existing.RoleId, N'platform.host_release_notes.read'
FROM dbo.fn_identity_role_permission AS existing
WHERE existing.PermissionCode = N'platform.release_notes.create'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS granted
    WHERE granted.RoleId = existing.RoleId
      AND granted.PermissionCode = N'platform.host_release_notes.read'
);

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT DISTINCT existing.RoleId, N'regions.administrative_regions.manage'
FROM dbo.fn_identity_role_permission AS existing
WHERE existing.PermissionCode = N'regions.administrative_regions.create'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS granted
    WHERE granted.RoleId = existing.RoleId
      AND granted.PermissionCode = N'regions.administrative_regions.manage'
);
