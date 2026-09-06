-- 170：为已具备 host_documents.download 权限的角色与 API Key 幂等授予 host_preview_tasks 权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT downloadRole.RoleId, N'document.host_preview_tasks.create'
FROM dbo.fn_identity_role_permission AS downloadRole
WHERE downloadRole.PermissionCode = N'document.host_documents.download'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = downloadRole.RoleId
      AND existing.PermissionCode = N'document.host_preview_tasks.create'
  );

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT readRole.RoleId, N'document.host_preview_tasks.read'
FROM dbo.fn_identity_role_permission AS readRole
WHERE readRole.PermissionCode = N'document.host_documents.read'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = readRole.RoleId
      AND existing.PermissionCode = N'document.host_preview_tasks.read'
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
            SELECT N'document.host_preview_tasks.create'
            FROM OPENJSON(apiKey.PermissionsJson) AS downloadProbe
            WHERE downloadProbe.value = N'document.host_documents.download'
            UNION ALL
            SELECT N'document.host_preview_tasks.read'
            FROM OPENJSON(apiKey.PermissionsJson) AS readProbe
            WHERE readProbe.value = N'document.host_documents.read'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE (
    apiKey.PermissionsJson LIKE N'%document.host_documents.download%'
    OR apiKey.PermissionsJson LIKE N'%document.host_documents.read%'
)
AND (
    apiKey.PermissionsJson NOT LIKE N'%document.host_preview_tasks.create%'
    OR apiKey.PermissionsJson NOT LIKE N'%document.host_preview_tasks.read%'
);
