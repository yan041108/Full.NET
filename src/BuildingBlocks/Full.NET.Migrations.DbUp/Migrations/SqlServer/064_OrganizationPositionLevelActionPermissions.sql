-- 064锛氬皢瀛橀噺 organization.position_levels.write 灞曞紑涓轰笁涓簿纭鎴锋満鏋勫姩浣滄潈闄愶紝骞傜瓑澶勭悊瑙掕壊琛屼笌 API Key JSON銆?
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'organization.position_levels.create'),
        (N'organization.position_levels.update'),
        (N'organization.position_levels.disable')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'organization.position_levels.write'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM dbo.fn_identity_role_permission
WHERE PermissionCode = N'organization.position_levels.write';

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
            WHERE element.value <> N'organization.position_levels.write'
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS writeProbe
            CROSS JOIN (
                VALUES
                    (N'organization.position_levels.create'),
                    (N'organization.position_levels.update'),
                    (N'organization.position_levels.disable')
            ) AS actions(PermissionCode)
            WHERE writeProbe.value = N'organization.position_levels.write'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%organization.position_levels.write%';