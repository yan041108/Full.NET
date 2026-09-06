-- 172：为已具备 organization.positions.import 权限的角色与 API Key 幂等授予 ImportExport 任务权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT importRole.RoleId, 'import_export.static_schemas.read'
FROM fn_identity_role_permission AS importRole
WHERE importRole.PermissionCode = 'organization.positions.import'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = importRole.RoleId
      AND existing.PermissionCode = 'import_export.static_schemas.read'
  );

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT importRole.RoleId, 'import_export.import_tasks.read'
FROM fn_identity_role_permission AS importRole
WHERE importRole.PermissionCode = 'organization.positions.import'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = importRole.RoleId
      AND existing.PermissionCode = 'import_export.import_tasks.read'
  );

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT importRole.RoleId, 'import_export.import_tasks.create'
FROM fn_identity_role_permission AS importRole
WHERE importRole.PermissionCode = 'organization.positions.import'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = importRole.RoleId
      AND existing.PermissionCode = 'import_export.import_tasks.create'
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
            SELECT expanded.Id, 'import_export.static_schemas.read' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'organization.positions.import'
            ) AS expanded
            UNION ALL
            SELECT expanded.Id, 'import_export.import_tasks.read' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'organization.positions.import'
            ) AS expanded
            UNION ALL
            SELECT expanded.Id, 'import_export.import_tasks.create' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'organization.positions.import'
            ) AS expanded
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%organization.positions.import%'
      AND (
        source.PermissionsJson NOT LIKE '%import_export.static_schemas.read%'
        OR source.PermissionsJson NOT LIKE '%import_export.import_tasks.read%'
        OR source.PermissionsJson NOT LIKE '%import_export.import_tasks.create%'
      )
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%organization.positions.import%'
  AND (
    apiKey.PermissionsJson NOT LIKE '%import_export.static_schemas.read%'
    OR apiKey.PermissionsJson NOT LIKE '%import_export.import_tasks.read%'
    OR apiKey.PermissionsJson NOT LIKE '%import_export.import_tasks.create%'
  );
