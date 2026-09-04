-- 107：为已具备 publish 权限的角色与 API Key 幂等授予 retract 权限。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT publish.RoleId, N'notifications.announcements.retract'
FROM dbo.fn_identity_role_permission AS publish
WHERE publish.PermissionCode = N'notifications.announcements.publish'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = publish.RoleId
      AND existing.PermissionCode = N'notifications.announcements.retract'
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
            SELECT N'notifications.announcements.retract'
            FROM OPENJSON(apiKey.PermissionsJson) AS publishProbe
            WHERE publishProbe.value = N'notifications.announcements.publish'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%notifications.announcements.publish%'
  AND apiKey.PermissionsJson NOT LIKE N'%notifications.announcements.retract%';
