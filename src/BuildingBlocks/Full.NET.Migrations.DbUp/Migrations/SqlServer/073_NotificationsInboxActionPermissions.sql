-- 073：将存量 notifications.inbox.write 展开为 send，并为存量 notifications.inbox.read 补齐 mark_read/mark_all_read。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'notifications.inbox.send')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'notifications.inbox.write'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM dbo.fn_identity_role_permission
WHERE PermissionCode = N'notifications.inbox.write';

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'notifications.inbox.mark_read'),
        (N'notifications.inbox.mark_all_read')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'notifications.inbox.read'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
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
            WHERE element.value <> N'notifications.inbox.write'
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS writeProbe
            CROSS JOIN (
                VALUES
                    (N'notifications.inbox.send')
            ) AS actions(PermissionCode)
            WHERE writeProbe.value = N'notifications.inbox.write'
            UNION ALL
            SELECT N'notifications.inbox.mark_read'
            FROM OPENJSON(apiKey.PermissionsJson) AS readProbe
            WHERE readProbe.value = N'notifications.inbox.read'
              AND NOT EXISTS (
                SELECT 1
                FROM OPENJSON(apiKey.PermissionsJson) AS markReadProbe
                WHERE markReadProbe.value = N'notifications.inbox.mark_read'
              )
            UNION ALL
            SELECT N'notifications.inbox.mark_all_read'
            FROM OPENJSON(apiKey.PermissionsJson) AS readProbe
            WHERE readProbe.value = N'notifications.inbox.read'
              AND NOT EXISTS (
                SELECT 1
                FROM OPENJSON(apiKey.PermissionsJson) AS markAllProbe
                WHERE markAllProbe.value = N'notifications.inbox.mark_all_read'
              )
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%notifications.inbox.read%'
   OR apiKey.PermissionsJson LIKE N'%notifications.inbox.write%';