-- 107：为已具备 publish 权限的角色与 API Key 幂等授予 retract 权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT publish.RoleId, 'notifications.announcements.retract'
FROM fn_identity_role_permission AS publish
WHERE publish.PermissionCode = 'notifications.announcements.publish'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = publish.RoleId
      AND existing.PermissionCode = 'notifications.announcements.retract'
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
                'notifications.announcements.retract' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS elements
                WHERE elements.raw = 'notifications.announcements.publish'
            ) AS expanded
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%notifications.announcements.publish%'
      AND source.PermissionsJson NOT LIKE '%notifications.announcements.retract%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%notifications.announcements.publish%'
  AND apiKey.PermissionsJson NOT LIKE '%notifications.announcements.retract%';
