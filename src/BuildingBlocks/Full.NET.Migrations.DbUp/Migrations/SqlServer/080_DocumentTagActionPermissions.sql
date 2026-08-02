-- 080：将存量 document.tags.manage 展开为 read/create/update/delete，并为存量 document.host_documents.read 补齐 document.tags.read。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'document.tags.read'),
        (N'document.tags.create'),
        (N'document.tags.update'),
        (N'document.tags.delete')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'document.tags.manage'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM dbo.fn_identity_role_permission
WHERE PermissionCode = N'document.tags.manage';

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, N'document.tags.read'
FROM dbo.fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = N'document.host_documents.read'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = N'document.tags.read'
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
            WHERE element.value <> N'document.tags.manage'
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS manageProbe
            CROSS JOIN (
                VALUES
                    (N'document.tags.read'),
                    (N'document.tags.create'),
                    (N'document.tags.update'),
                    (N'document.tags.delete')
            ) AS actions(PermissionCode)
            WHERE manageProbe.value = N'document.tags.manage'
            UNION ALL
            SELECT N'document.tags.read'
            FROM OPENJSON(apiKey.PermissionsJson) AS readProbe
            WHERE readProbe.value = N'document.host_documents.read'
              AND NOT EXISTS (
                SELECT 1
                FROM OPENJSON(apiKey.PermissionsJson) AS tagReadProbe
                WHERE tagReadProbe.value = N'document.tags.read'
              )
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%document.tags.manage%'
   OR apiKey.PermissionsJson LIKE N'%document.host_documents.read%';
