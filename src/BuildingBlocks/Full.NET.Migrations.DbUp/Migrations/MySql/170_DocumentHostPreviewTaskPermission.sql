-- 170：为已具备 host_documents.download 权限的角色与 API Key 幂等授予 host_preview_tasks 权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT downloadRole.RoleId, 'document.host_preview_tasks.create'
FROM fn_identity_role_permission AS downloadRole
WHERE downloadRole.PermissionCode = 'document.host_documents.download'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = downloadRole.RoleId
      AND existing.PermissionCode = 'document.host_preview_tasks.create'
  );

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT readRole.RoleId, 'document.host_preview_tasks.read'
FROM fn_identity_role_permission AS readRole
WHERE readRole.PermissionCode = 'document.host_documents.read'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = readRole.RoleId
      AND existing.PermissionCode = 'document.host_preview_tasks.read'
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
            SELECT expanded.Id, 'document.host_preview_tasks.create' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'document.host_documents.download'
            ) AS expanded
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%document.host_documents.download%'
      AND source.PermissionsJson NOT LIKE '%document.host_preview_tasks.create%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%document.host_documents.download%'
  AND apiKey.PermissionsJson NOT LIKE '%document.host_preview_tasks.create%';

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
            SELECT expanded.Id, 'document.host_preview_tasks.read' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'document.host_documents.read'
            ) AS expanded
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%document.host_documents.read%'
      AND source.PermissionsJson NOT LIKE '%document.host_preview_tasks.read%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%document.host_documents.read%'
  AND apiKey.PermissionsJson NOT LIKE '%document.host_preview_tasks.read%';
