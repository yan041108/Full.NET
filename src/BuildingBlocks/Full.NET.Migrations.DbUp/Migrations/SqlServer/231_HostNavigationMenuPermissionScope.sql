-- 231：将 Host 专属管理页导航权限与代码目录对齐（租户上下文导航裁剪依赖 RequiredPermission 作用域）。
UPDATE dbo.fn_identity_navigation
SET RequiredPermission = N'platform.host_release_notes.read',
    UpdatedAtUtc = SYSUTCDATETIME(),
    Version = Version + 1
WHERE ScopeKey = N'host'
  AND TenantId IS NULL
  AND RouteName = N'host-release-notes'
  AND RequiredPermission <> N'platform.host_release_notes.read';

UPDATE dbo.fn_identity_navigation
SET RequiredPermission = N'regions.administrative_regions.manage',
    UpdatedAtUtc = SYSUTCDATETIME(),
    Version = Version + 1
WHERE ScopeKey = N'host'
  AND TenantId IS NULL
  AND RouteName = N'administrative-regions'
  AND RequiredPermission <> N'regions.administrative_regions.manage';
