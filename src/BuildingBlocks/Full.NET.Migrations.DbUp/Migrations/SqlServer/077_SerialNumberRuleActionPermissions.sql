-- 077：将存量 serial_numbers.rules.write 展开为 create/update/enable/disable，并为存量 serial_numbers.rules.read 补齐 preview。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'serial_numbers.rules.create'),
        (N'serial_numbers.rules.update'),
        (N'serial_numbers.rules.enable'),
        (N'serial_numbers.rules.disable')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'serial_numbers.rules.write'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM dbo.fn_identity_role_permission
WHERE PermissionCode = N'serial_numbers.rules.write';

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, N'serial_numbers.rules.preview'
FROM dbo.fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = N'serial_numbers.rules.read'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = N'serial_numbers.rules.preview'
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
            WHERE element.value <> N'serial_numbers.rules.write'
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS writeProbe
            CROSS JOIN (
                VALUES
                    (N'serial_numbers.rules.create'),
                    (N'serial_numbers.rules.update'),
                    (N'serial_numbers.rules.enable'),
                    (N'serial_numbers.rules.disable')
            ) AS actions(PermissionCode)
            WHERE writeProbe.value = N'serial_numbers.rules.write'
            UNION ALL
            SELECT N'serial_numbers.rules.preview'
            FROM OPENJSON(apiKey.PermissionsJson) AS readProbe
            WHERE readProbe.value = N'serial_numbers.rules.read'
              AND NOT EXISTS (
                SELECT 1
                FROM OPENJSON(apiKey.PermissionsJson) AS previewProbe
                WHERE previewProbe.value = N'serial_numbers.rules.preview'
              )
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%serial_numbers.rules.read%'
   OR apiKey.PermissionsJson LIKE N'%serial_numbers.rules.write%';