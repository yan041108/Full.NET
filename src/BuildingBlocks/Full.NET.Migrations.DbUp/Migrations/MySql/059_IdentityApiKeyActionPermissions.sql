-- 059：将存量 identity.api_keys.write 展开为三个精确 Host API Key 动作权限，幂等处理角色行与 API Key JSON。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM fn_identity_role_permission AS legacy
INNER JOIN (
    SELECT 'identity.api_keys.create' AS PermissionCode
    UNION ALL SELECT 'identity.api_keys.disable'
    UNION ALL SELECT 'identity.api_keys.rotate'
) AS actions
WHERE legacy.PermissionCode = 'identity.api_keys.write'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM fn_identity_role_permission
WHERE PermissionCode = 'identity.api_keys.write';

UPDATE fn_identity_api_key AS apiKey
INNER JOIN (
    SELECT
        source.Id,
        CAST(
            CONCAT(
                '[',
                GROUP_CONCAT(JSON_QUOTE(mapped.elem) ORDER BY mapped.elem SEPARATOR ','),
                ']')
            AS JSON) AS PermissionsJson
    FROM fn_identity_api_key AS source
    INNER JOIN (
        SELECT
            distinctMapped.Id,
            distinctMapped.elem
        FROM (
            SELECT
                preserved.Id,
                preserved.elem
            FROM (
                SELECT
                    sourceInner.Id,
                    elements.raw AS elem
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS elements
                WHERE elements.raw <> 'identity.api_keys.write'
            ) AS preserved
            UNION ALL
            SELECT
                expanded.Id,
                actionCodes.PermissionCode AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS elements
                WHERE elements.raw = 'identity.api_keys.write'
            ) AS expanded
            CROSS JOIN (
                SELECT 'identity.api_keys.create' AS PermissionCode
                UNION ALL SELECT 'identity.api_keys.disable'
                UNION ALL SELECT 'identity.api_keys.rotate'
            ) AS actionCodes
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%identity.api_keys.write%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%identity.api_keys.write%';