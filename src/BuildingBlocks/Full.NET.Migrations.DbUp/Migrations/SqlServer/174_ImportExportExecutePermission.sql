-- 174：为已具备 import_export.import_tasks.create 权限的角色与 API Key 幂等授予 execute 权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT createRole.RoleId, N'import_export.import_tasks.execute'
FROM dbo.fn_identity_role_permission AS createRole
WHERE createRole.PermissionCode = N'import_export.import_tasks.create'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = createRole.RoleId
      AND existing.PermissionCode = N'import_export.import_tasks.execute'
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
            SELECT N'import_export.import_tasks.execute'
            FROM OPENJSON(apiKey.PermissionsJson) AS createProbe
            WHERE createProbe.value = N'import_export.import_tasks.create'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%import_export.import_tasks.create%'
  AND apiKey.PermissionsJson NOT LIKE N'%import_export.import_tasks.execute%';
