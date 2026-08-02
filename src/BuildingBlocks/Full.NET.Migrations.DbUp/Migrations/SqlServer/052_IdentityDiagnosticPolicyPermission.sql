-- 052：将诊断策略权限码从连字符形态迁移为 lower_snake 规范形态，幂等收敛重复授权与 API Key JSON。

DELETE rp
FROM dbo.fn_identity_role_permission AS rp
WHERE rp.PermissionCode = N'settings.diagnostic-policy.read'
  AND EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = rp.RoleId
      AND existing.PermissionCode = N'settings.diagnostic_policy.read'
  );

UPDATE dbo.fn_identity_role_permission
SET PermissionCode = N'settings.diagnostic_policy.read'
WHERE PermissionCode = N'settings.diagnostic-policy.read';

DELETE rp
FROM dbo.fn_identity_role_permission AS rp
WHERE rp.PermissionCode = N'settings.diagnostic-policy.write'
  AND EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = rp.RoleId
      AND existing.PermissionCode = N'settings.diagnostic_policy.write'
  );

UPDATE dbo.fn_identity_role_permission
SET PermissionCode = N'settings.diagnostic_policy.write'
WHERE PermissionCode = N'settings.diagnostic-policy.write';

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
        SELECT
            transformed.SortKey,
            transformed.QuotedValue AS Value
        FROM (
            SELECT
                CAST(element.[key] AS int) AS SortKey,
                CASE
                    WHEN element.value = N'settings.diagnostic-policy.read'
                         AND EXISTS (
                             SELECT 1
                             FROM OPENJSON(apiKey.PermissionsJson) AS probe
                             WHERE probe.value = N'settings.diagnostic_policy.read'
                         ) THEN NULL
                    WHEN element.value = N'settings.diagnostic-policy.read'
                        THEN N'"settings.diagnostic_policy.read"'
                    WHEN element.value = N'settings.diagnostic-policy.write'
                         AND EXISTS (
                             SELECT 1
                             FROM OPENJSON(apiKey.PermissionsJson) AS probe
                             WHERE probe.value = N'settings.diagnostic_policy.write'
                         ) THEN NULL
                    WHEN element.value = N'settings.diagnostic-policy.write'
                        THEN N'"settings.diagnostic_policy.write"'
                    ELSE N'"' + STRING_ESCAPE(element.value, N'json') + N'"'
                END AS QuotedValue
            FROM OPENJSON(apiKey.PermissionsJson) AS element
        ) AS transformed
        WHERE transformed.QuotedValue IS NOT NULL
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%settings.diagnostic-policy.%';
