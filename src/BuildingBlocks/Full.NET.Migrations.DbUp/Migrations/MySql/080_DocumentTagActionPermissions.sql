-- 080：将存量 document.tags.manage 展开为 read/create/update/delete，并为存量 document.host_documents.read 补齐 document.tags.read。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM fn_identity_role_permission AS legacy
INNER JOIN (
    SELECT 'document.tags.read' AS PermissionCode
    UNION ALL SELECT 'document.tags.create'
    UNION ALL SELECT 'document.tags.update'
    UNION ALL SELECT 'document.tags.delete'
) AS actions
WHERE legacy.PermissionCode = 'document.tags.manage'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM fn_identity_role_permission
WHERE PermissionCode = 'document.tags.manage';

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, 'document.tags.read'
FROM fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = 'document.host_documents.read'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = 'document.tags.read'
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
                WHERE elements.raw <> 'document.tags.manage'
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
                WHERE elements.raw = 'document.tags.manage'
            ) AS expanded
            CROSS JOIN (
                SELECT 'document.tags.read' AS PermissionCode
                UNION ALL SELECT 'document.tags.create'
                UNION ALL SELECT 'document.tags.update'
                UNION ALL SELECT 'document.tags.delete'
            ) AS actionCodes
            UNION ALL
            SELECT
                readExpanded.Id,
                'document.tags.read' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS elements
                WHERE elements.raw = 'document.host_documents.read'
            ) AS readExpanded
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_identity_api_key AS tagReadSource
                CROSS JOIN JSON_TABLE(
                    tagReadSource.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS tagReadElements
                WHERE tagReadSource.Id = readExpanded.Id
                  AND tagReadElements.raw = 'document.tags.read'
            )
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%document.tags.manage%'
       OR source.PermissionsJson LIKE '%document.host_documents.read%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%document.tags.manage%'
   OR apiKey.PermissionsJson LIKE '%document.host_documents.read%';
