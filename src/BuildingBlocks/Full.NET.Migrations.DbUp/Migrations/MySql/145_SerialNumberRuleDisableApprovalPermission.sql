-- 145：为已具备 disable 权限的角色与 API Key 幂等授予 submit_disable_approval 权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT disable.RoleId, 'serial_numbers.rules.submit_disable_approval'
FROM fn_identity_role_permission AS disable
WHERE disable.PermissionCode = 'serial_numbers.rules.disable'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = disable.RoleId
      AND existing.PermissionCode = 'serial_numbers.rules.submit_disable_approval'
  );

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
            ) AS preserved
            UNION ALL
            SELECT
                expanded.Id,
                'serial_numbers.rules.submit_disable_approval' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS elements
                WHERE elements.raw = 'serial_numbers.rules.disable'
            ) AS expanded
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%serial_numbers.rules.disable%'
      AND source.PermissionsJson NOT LIKE '%serial_numbers.rules.submit_disable_approval%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%serial_numbers.rules.disable%'
  AND apiKey.PermissionsJson NOT LIKE '%serial_numbers.rules.submit_disable_approval%';
