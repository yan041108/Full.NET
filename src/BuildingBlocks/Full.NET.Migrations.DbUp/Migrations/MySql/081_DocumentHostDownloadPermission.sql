-- 081：为存量 document.host_documents.read 补齐 document.host_documents.download。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, 'document.host_documents.download'
FROM fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = 'document.host_documents.read'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = 'document.host_documents.download'
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
        SELECT DISTINCT mappedInner.Id, mappedInner.elem
        FROM (
            SELECT preserved.Id, preserved.elem
            FROM (
                SELECT sourceInner.Id, elements.raw AS elem
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
            ) AS preserved
            UNION ALL
            SELECT readExpanded.Id, 'document.host_documents.download' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'document.host_documents.read'
            ) AS readExpanded
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_identity_api_key AS downloadSource
                CROSS JOIN JSON_TABLE(
                    downloadSource.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS downloadElements
                WHERE downloadSource.Id = readExpanded.Id
                  AND downloadElements.raw = 'document.host_documents.download'
            )
        ) AS mappedInner
    ) AS mapped ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%document.host_documents.read%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%document.host_documents.read%';