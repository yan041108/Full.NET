-- 079：将存量 document.categories.manage 展开为 read/create/update/delete，并为存量 document.host_documents.read 补齐 document.categories.read。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'document.categories.read'),
        (N'document.categories.create'),
        (N'document.categories.update'),
        (N'document.categories.delete')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'document.categories.manage'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM dbo.fn_identity_role_permission
WHERE PermissionCode = N'document.categories.manage';

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, N'document.categories.read'
FROM dbo.fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = N'document.host_documents.read'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = N'document.categories.read'
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
            WHERE element.value <> N'document.categories.manage'
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS manageProbe
            CROSS JOIN (
                VALUES
                    (N'document.categories.read'),
                    (N'document.categories.create'),
                    (N'document.categories.update'),
                    (N'document.categories.delete')
            ) AS actions(PermissionCode)
            WHERE manageProbe.value = N'document.categories.manage'
            UNION ALL
            SELECT N'document.categories.read'
            FROM OPENJSON(apiKey.PermissionsJson) AS readProbe
            WHERE readProbe.value = N'document.host_documents.read'
              AND NOT EXISTS (
                SELECT 1
                FROM OPENJSON(apiKey.PermissionsJson) AS categoryReadProbe
                WHERE categoryReadProbe.value = N'document.categories.read'
              )
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%document.categories.manage%'
   OR apiKey.PermissionsJson LIKE N'%document.host_documents.read%';