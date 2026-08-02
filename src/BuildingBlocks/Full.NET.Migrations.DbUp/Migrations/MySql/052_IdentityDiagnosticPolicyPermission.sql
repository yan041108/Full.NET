-- 052：将诊断策略权限码从连字符形态迁移为 lower_snake 规范形态，幂等收敛重复授权与 API Key JSON。

DELETE legacy
FROM fn_identity_role_permission AS legacy
INNER JOIN fn_identity_role_permission AS existing
    ON existing.RoleId = legacy.RoleId
   AND existing.PermissionCode = 'settings.diagnostic_policy.read'
WHERE legacy.PermissionCode = 'settings.diagnostic-policy.read';

UPDATE fn_identity_role_permission
SET PermissionCode = 'settings.diagnostic_policy.read'
WHERE PermissionCode = 'settings.diagnostic-policy.read';

DELETE legacy
FROM fn_identity_role_permission AS legacy
INNER JOIN fn_identity_role_permission AS existing
    ON existing.RoleId = legacy.RoleId
   AND existing.PermissionCode = 'settings.diagnostic_policy.write'
WHERE legacy.PermissionCode = 'settings.diagnostic-policy.write';

UPDATE fn_identity_role_permission
SET PermissionCode = 'settings.diagnostic_policy.write'
WHERE PermissionCode = 'settings.diagnostic-policy.write';

UPDATE fn_identity_api_key AS apiKey
INNER JOIN (
    SELECT
        source.Id,
        CAST(
            CONCAT(
                '[',
                GROUP_CONCAT(JSON_QUOTE(mapped.elem) ORDER BY mapped.sortKey SEPARATOR ','),
                ']')
            AS JSON) AS PermissionsJson
    FROM fn_identity_api_key AS source
    INNER JOIN (
        SELECT
            sourceInner.Id,
            elements.sortKey,
            CASE
                WHEN elements.raw = 'settings.diagnostic-policy.read'
                     AND JSON_CONTAINS(
                         sourceInner.PermissionsJson,
                         '"settings.diagnostic_policy.read"',
                         '$') THEN NULL
                WHEN elements.raw = 'settings.diagnostic-policy.read'
                    THEN 'settings.diagnostic_policy.read'
                WHEN elements.raw = 'settings.diagnostic-policy.write'
                     AND JSON_CONTAINS(
                         sourceInner.PermissionsJson,
                         '"settings.diagnostic_policy.write"',
                         '$') THEN NULL
                WHEN elements.raw = 'settings.diagnostic-policy.write'
                    THEN 'settings.diagnostic_policy.write'
                ELSE elements.raw
            END AS elem
        FROM fn_identity_api_key AS sourceInner
        CROSS JOIN JSON_TABLE(
            sourceInner.PermissionsJson,
            '$[*]' COLUMNS (
                sortKey FOR ORDINALITY,
                raw VARCHAR(160) PATH '$'
            )
        ) AS elements
    ) AS mapped
        ON mapped.Id = source.Id
       AND mapped.elem IS NOT NULL
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%settings.diagnostic-policy.%';
