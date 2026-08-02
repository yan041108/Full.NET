-- 073：将存量 notifications.inbox.write 展开为 send，并为存量 notifications.inbox.read 补齐 mark_read/mark_all_read。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM fn_identity_role_permission AS legacy
INNER JOIN (
    SELECT 'notifications.inbox.send' AS PermissionCode
) AS actions
WHERE legacy.PermissionCode = 'notifications.inbox.write'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM fn_identity_role_permission
WHERE PermissionCode = 'notifications.inbox.write';

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM fn_identity_role_permission AS legacy
INNER JOIN (
    SELECT 'notifications.inbox.mark_read' AS PermissionCode
    UNION ALL SELECT 'notifications.inbox.mark_all_read'
) AS actions
WHERE legacy.PermissionCode = 'notifications.inbox.read'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
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
                WHERE elements.raw <> 'notifications.inbox.write'
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
                WHERE elements.raw = 'notifications.inbox.write'
            ) AS expanded
            CROSS JOIN (
                SELECT 'notifications.inbox.send' AS PermissionCode
            ) AS actionCodes
            UNION ALL
            SELECT
                readExpanded.Id,
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
                WHERE elements.raw = 'notifications.inbox.read'
            ) AS readExpanded
            CROSS JOIN (
                SELECT 'notifications.inbox.mark_read' AS PermissionCode
                UNION ALL SELECT 'notifications.inbox.mark_all_read'
            ) AS actionCodes
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_identity_api_key AS probeKey
                CROSS JOIN JSON_TABLE(
                    probeKey.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS downloadProbe
                WHERE probeKey.Id = readExpanded.Id
                  AND downloadProbe.raw = actionCodes.PermissionCode
            )
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%notifications.inbox.read%'
       OR source.PermissionsJson LIKE '%notifications.inbox.write%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%notifications.inbox.read%'
   OR apiKey.PermissionsJson LIKE '%notifications.inbox.write%';