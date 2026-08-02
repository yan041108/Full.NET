-- 078：将存量 document.host_documents.write 展开为 create/update/add_version，并为存量 document.host_documents.delete 补齐 restore。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'document.host_documents.create'),
        (N'document.host_documents.update'),
        (N'document.host_documents.add_version')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'document.host_documents.write'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM dbo.fn_identity_role_permission
WHERE PermissionCode = N'document.host_documents.write';

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, N'document.host_documents.restore'
FROM dbo.fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = N'document.host_documents.delete'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = N'document.host_documents.restore'
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
            WHERE element.value <> N'document.host_documents.write'
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS writeProbe
            CROSS JOIN (
                VALUES
                    (N'document.host_documents.create'),
                    (N'document.host_documents.update'),
                    (N'document.host_documents.add_version')
            ) AS actions(PermissionCode)
            WHERE writeProbe.value = N'document.host_documents.write'
            UNION ALL
            SELECT N'document.host_documents.restore'
            FROM OPENJSON(apiKey.PermissionsJson) AS deleteProbe
            WHERE deleteProbe.value = N'document.host_documents.delete'
              AND NOT EXISTS (
                SELECT 1
                FROM OPENJSON(apiKey.PermissionsJson) AS restoreProbe
                WHERE restoreProbe.value = N'document.host_documents.restore'
              )
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%document.host_documents.read%'
   OR apiKey.PermissionsJson LIKE N'%document.host_documents.write%'
   OR apiKey.PermissionsJson LIKE N'%document.host_documents.delete%';