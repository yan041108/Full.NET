-- 231：将 Host 专属管理页导航权限与代码目录对齐（租户上下文导航裁剪依赖 RequiredPermission 作用域）。
UPDATE fn_identity_navigation
SET RequiredPermission = 'platform.host_release_notes.read',
    UpdatedAtUtc = UTC_TIMESTAMP(6),
    Version = Version + 1
WHERE ScopeKey = 'host'
  AND TenantId IS NULL
  AND RouteName = 'host-release-notes'
  AND RequiredPermission <> 'platform.host_release_notes.read';

UPDATE fn_identity_navigation
SET RequiredPermission = 'regions.administrative_regions.manage',
    UpdatedAtUtc = UTC_TIMESTAMP(6),
    Version = Version + 1
WHERE ScopeKey = 'host'
  AND TenantId IS NULL
  AND RouteName = 'administrative-regions'
  AND RequiredPermission <> 'regions.administrative_regions.manage';
