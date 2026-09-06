-- 172：为已具备 organization.positions.import 权限的角色与 API Key 幂等授予 ImportExport 任务权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT importRole.RoleId, N'import_export.static_schemas.read'
FROM dbo.fn_identity_role_permission AS importRole
WHERE importRole.PermissionCode = N'organization.positions.import'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = importRole.RoleId
      AND existing.PermissionCode = N'import_export.static_schemas.read'
  );

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT importRole.RoleId, N'import_export.import_tasks.read'
FROM dbo.fn_identity_role_permission AS importRole
WHERE importRole.PermissionCode = N'organization.positions.import'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = importRole.RoleId
      AND existing.PermissionCode = N'import_export.import_tasks.read'
  );

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT importRole.RoleId, N'import_export.import_tasks.create'
FROM dbo.fn_identity_role_permission AS importRole
WHERE importRole.PermissionCode = N'organization.positions.import'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = importRole.RoleId
      AND existing.PermissionCode = N'import_export.import_tasks.create'
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
            SELECT N'import_export.static_schemas.read'
            FROM OPENJSON(apiKey.PermissionsJson) AS importProbe
            WHERE importProbe.value = N'organization.positions.import'
            UNION ALL
            SELECT N'import_export.import_tasks.read'
            FROM OPENJSON(apiKey.PermissionsJson) AS importProbe
            WHERE importProbe.value = N'organization.positions.import'
            UNION ALL
            SELECT N'import_export.import_tasks.create'
            FROM OPENJSON(apiKey.PermissionsJson) AS importProbe
            WHERE importProbe.value = N'organization.positions.import'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%organization.positions.import%'
  AND (
    apiKey.PermissionsJson NOT LIKE N'%import_export.static_schemas.read%'
    OR apiKey.PermissionsJson NOT LIKE N'%import_export.import_tasks.read%'
    OR apiKey.PermissionsJson NOT LIKE N'%import_export.import_tasks.create%'
  );
