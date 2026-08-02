-- 060：将存量 tenancy.tenants.write 展开为四个精确 Host 租户动作权限，幂等处理角色行与 API Key JSON。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'tenancy.tenants.create'),
        (N'tenancy.tenants.update'),
        (N'tenancy.tenants.disable'),
        (N'tenancy.tenants.assign_package')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'tenancy.tenants.write'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM dbo.fn_identity_role_permission
WHERE PermissionCode = N'tenancy.tenants.write';

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
            WHERE element.value <> N'tenancy.tenants.write'
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS writeProbe
            CROSS JOIN (
                VALUES
                    (N'tenancy.tenants.create'),
                    (N'tenancy.tenants.update'),
                    (N'tenancy.tenants.disable'),
                    (N'tenancy.tenants.assign_package')
            ) AS actions(PermissionCode)
            WHERE writeProbe.value = N'tenancy.tenants.write'
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%tenancy.tenants.write%';