-- 131：为 Host 文件虚拟目录、元数据更新与引用查询补齐可分配权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM fn_identity_role_permission AS legacy
INNER JOIN (
    SELECT 'files.files.update' AS PermissionCode
    UNION ALL SELECT 'files.file_references.read'
    UNION ALL SELECT 'files.folders.create'
    UNION ALL SELECT 'files.folders.update'
    UNION ALL SELECT 'files.folders.delete'
) AS actions
WHERE legacy.PermissionCode = 'files.files.read'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, 'files.files.update'
FROM fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = 'files.files.upload'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = 'files.files.update'
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
            SELECT readExpanded.Id, actionCodes.PermissionCode AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'files.files.read'
            ) AS readExpanded
            CROSS JOIN (
                SELECT 'files.files.update' AS PermissionCode
                UNION ALL SELECT 'files.file_references.read'
                UNION ALL SELECT 'files.folders.create'
                UNION ALL SELECT 'files.folders.update'
                UNION ALL SELECT 'files.folders.delete'
            ) AS actionCodes
            UNION ALL
            SELECT uploadExpanded.Id, 'files.files.update' AS elem
            FROM (
                SELECT sourceInner.Id
                FROM fn_identity_api_key AS sourceInner
                CROSS JOIN JSON_TABLE(
                    sourceInner.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS elements
                WHERE elements.raw = 'files.files.upload'
            ) AS uploadExpanded
            WHERE NOT EXISTS (
                SELECT 1
                FROM fn_identity_api_key AS probeKey
                CROSS JOIN JSON_TABLE(
                    probeKey.PermissionsJson,
                    '$[*]' COLUMNS (raw VARCHAR(160) PATH '$')
                ) AS updateProbe
                WHERE probeKey.Id = uploadExpanded.Id
                  AND updateProbe.raw = 'files.files.update'
            )
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%files.files.read%'
       OR source.PermissionsJson LIKE '%files.files.upload%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%files.files.read%'
   OR apiKey.PermissionsJson LIKE '%files.files.upload%';
