-- 099：将 identity.super_administrators.manage 拆分为 grant/revoke 并退役 manage。
INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'identity.super_administrators.grant'),
        (N'identity.super_administrators.revoke')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'identity.super_administrators.manage'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM dbo.fn_identity_role_permission
WHERE PermissionCode = N'identity.super_administrators.manage';

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
            WHERE element.value <> N'identity.super_administrators.manage'
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS writeProbe
            CROSS JOIN (
                VALUES
                    (N'identity.super_administrators.grant'),
                    (N'identity.super_administrators.revoke')
            ) AS actions(PermissionCode)
            WHERE writeProbe.value = N'identity.super_administrators.manage'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE EXISTS (
    SELECT 1
    FROM OPENJSON(apiKey.PermissionsJson) AS probe
    WHERE probe.value = N'identity.super_administrators.manage'
);
