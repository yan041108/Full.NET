-- 078：将存量 document.host_documents.write 展开为 create/update/add_version，并为存量 document.host_documents.delete 补齐 restore。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM fn_identity_role_permission AS legacy
INNER JOIN (
    SELECT 'document.host_documents.create' AS PermissionCode
    UNION ALL SELECT 'document.host_documents.update'
    UNION ALL SELECT 'document.host_documents.add_version'
) AS actions
WHERE legacy.PermissionCode = 'document.host_documents.write'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM fn_identity_role_permission
WHERE PermissionCode = 'document.host_documents.write';

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, 'document.host_documents.restore'
FROM fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = 'document.host_documents.delete'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = 'document.host_documents.restore'
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
                WHERE elements.raw <> 'document.host_documents.write'
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
                WHERE elements.raw = 'document.host_documents.write'
            ) AS expanded
            CROSS JOIN (
                SELECT 'document.host_documents.create' AS PermissionCode
                UNION ALL SELECT 'document.host_documents.update'
                UNION ALL SELECT 'document.host_documents.add_version'
            ) AS actionCodes
            UNION ALL
            SELECT
                deleteExpanded.Id,
                'document.host_documents.restore' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS elements
                WHERE elements.raw = 'document.host_documents.delete'
            ) AS deleteExpanded
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_identity_api_key AS restoreSource
                CROSS JOIN JSON_TABLE(
                    restoreSource.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS restoreElements
                WHERE restoreSource.Id = deleteExpanded.Id
                  AND restoreElements.raw = 'document.host_documents.restore'
            )
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%document.host_documents.write%'
       OR source.PermissionsJson LIKE '%document.host_documents.delete%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%document.host_documents.write%'
   OR apiKey.PermissionsJson LIKE '%document.host_documents.delete%';