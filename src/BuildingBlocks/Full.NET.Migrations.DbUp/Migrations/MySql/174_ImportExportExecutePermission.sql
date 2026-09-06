-- 174：为已具备 import_export.import_tasks.create 权限的角色与 API Key 幂等授予 execute 权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT createRole.RoleId, 'import_export.import_tasks.execute'
FROM fn_identity_role_permission AS createRole
WHERE createRole.PermissionCode = 'import_export.import_tasks.create'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = createRole.RoleId
      AND existing.PermissionCode = 'import_export.import_tasks.execute'
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
            SELECT expanded.Id, 'import_export.import_tasks.execute' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'import_export.import_tasks.create'
            ) AS expanded
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%import_export.import_tasks.create%'
      AND source.PermissionsJson NOT LIKE '%import_export.import_tasks.execute%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%import_export.import_tasks.create%'
  AND apiKey.PermissionsJson NOT LIKE '%import_export.import_tasks.execute%';
