-- 077：将存量 serial_numbers.rules.write 展开为 create/update/enable/disable，并为存量 serial_numbers.rules.read 补齐 preview。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM fn_identity_role_permission AS legacy
INNER JOIN (
    SELECT 'serial_numbers.rules.create' AS PermissionCode
    UNION ALL SELECT 'serial_numbers.rules.update'
    UNION ALL SELECT 'serial_numbers.rules.enable'
    UNION ALL SELECT 'serial_numbers.rules.disable'
) AS actions
WHERE legacy.PermissionCode = 'serial_numbers.rules.write'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM fn_identity_role_permission
WHERE PermissionCode = 'serial_numbers.rules.write';

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, 'serial_numbers.rules.preview'
FROM fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = 'serial_numbers.rules.read'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = 'serial_numbers.rules.preview'
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
                WHERE elements.raw <> 'serial_numbers.rules.write'
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
                WHERE elements.raw = 'serial_numbers.rules.write'
            ) AS expanded
            CROSS JOIN (
                SELECT 'serial_numbers.rules.create' AS PermissionCode
                UNION ALL SELECT 'serial_numbers.rules.update'
                UNION ALL SELECT 'serial_numbers.rules.enable'
                UNION ALL SELECT 'serial_numbers.rules.disable'
            ) AS actionCodes
            UNION ALL
            SELECT
                readExpanded.Id,
                'serial_numbers.rules.preview' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS elements
                WHERE elements.raw = 'serial_numbers.rules.read'
            ) AS readExpanded
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_identity_api_key AS previewSource
                CROSS JOIN JSON_TABLE(
                    previewSource.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS previewElements
                WHERE previewSource.Id = readExpanded.Id
                  AND previewElements.raw = 'serial_numbers.rules.preview'
            )
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%serial_numbers.rules.read%'
       OR source.PermissionsJson LIKE '%serial_numbers.rules.write%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%serial_numbers.rules.read%'
   OR apiKey.PermissionsJson LIKE '%serial_numbers.rules.write%';