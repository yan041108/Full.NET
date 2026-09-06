-- 168：为已具备 host_statistics.read 权限的角色与 API Key 幂等授予 host_access_logs.read 权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT statisticsRole.RoleId, N'document.host_access_logs.read'
FROM dbo.fn_identity_role_permission AS statisticsRole
WHERE statisticsRole.PermissionCode = N'document.host_statistics.read'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = statisticsRole.RoleId
      AND existing.PermissionCode = N'document.host_access_logs.read'
  );

UPDATE dbo.fn_identity_api_key
SET PermissionsJson = rebuilt.Json
FROM dbo.fn_identity_api_key AS apiKey
CROSS APPLY (
    SELECT
        CASE
            WHEN COUNT(*) = 0 THEN N'[]'
            ELSE N'[' + STRING_AGG(quoted.Value, N',') WITHIN GROUP (ORDER BY quoted.SortKey) + N']'
        END AS Json
    FROM (
        SELECT DISTINCT
            permissions.PermissionCode AS SortKey,
            N'"' + STRING_ESCAPE(permissions.PermissionCode, N'json') + N'"' AS Value
        FROM (
            SELECT CAST(element.value AS nvarchar(128)) AS PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS element
            UNION ALL
            SELECT N'document.host_access_logs.read'
            FROM OPENJSON(apiKey.PermissionsJson) AS statisticsProbe
            WHERE statisticsProbe.value = N'document.host_statistics.read'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%document.host_statistics.read%'
  AND apiKey.PermissionsJson NOT LIKE N'%document.host_access_logs.read%';
