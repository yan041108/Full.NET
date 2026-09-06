-- 145：为已具备 disable 权限的角色与 API Key 幂等授予 submit_disable_approval 权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT disable.RoleId, N'serial_numbers.rules.submit_disable_approval'
FROM dbo.fn_identity_role_permission AS disable
WHERE disable.PermissionCode = N'serial_numbers.rules.disable'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = disable.RoleId
      AND existing.PermissionCode = N'serial_numbers.rules.submit_disable_approval'
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
            SELECT N'serial_numbers.rules.submit_disable_approval'
            FROM OPENJSON(apiKey.PermissionsJson) AS disableProbe
            WHERE disableProbe.value = N'serial_numbers.rules.disable'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%serial_numbers.rules.disable%'
  AND apiKey.PermissionsJson NOT LIKE N'%serial_numbers.rules.submit_disable_approval%';
