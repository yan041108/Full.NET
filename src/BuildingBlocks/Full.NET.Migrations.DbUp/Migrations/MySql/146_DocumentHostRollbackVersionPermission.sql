-- 146：为已具备 update 权限的角色与 API Key 幂等授予 rollback_version 权限。
INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT updateRole.RoleId, 'document.host_documents.rollback_version'
FROM fn_identity_role_permission AS updateRole
WHERE updateRole.PermissionCode = 'document.host_documents.update'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = updateRole.RoleId
      AND existing.PermissionCode = 'document.host_documents.rollback_version'
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
                'document.host_documents.rollback_version' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (
                        raw VARCHAR(160) PATH '$'
                    )
                ) AS elements
                WHERE elements.raw = 'document.host_documents.update'
            ) AS expanded
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%document.host_documents.update%'
      AND source.PermissionsJson NOT LIKE '%document.host_documents.rollback_version%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%document.host_documents.update%'
  AND apiKey.PermissionsJson NOT LIKE '%document.host_documents.rollback_version%';
