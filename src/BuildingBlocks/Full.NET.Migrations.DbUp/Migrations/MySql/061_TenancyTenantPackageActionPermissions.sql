-- 061：将存量 tenancy.tenant_packages.write 展开为三个精确 Host 租户套餐动作权限，幂等处理角色行与 API Key JSON。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM fn_identity_role_permission AS legacy
INNER JOIN (
    SELECT 'tenancy.tenant_packages.create' AS PermissionCode
    UNION ALL SELECT 'tenancy.tenant_packages.update'
    UNION ALL SELECT 'tenancy.tenant_packages.disable'
) AS actions
WHERE legacy.PermissionCode = 'tenancy.tenant_packages.write'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

DELETE FROM fn_identity_role_permission
WHERE PermissionCode = 'tenancy.tenant_packages.write';

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
                WHERE elements.raw <> 'tenancy.tenant_packages.write'
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
                WHERE elements.raw = 'tenancy.tenant_packages.write'
            ) AS expanded
            CROSS JOIN (
                SELECT 'tenancy.tenant_packages.create' AS PermissionCode
                UNION ALL SELECT 'tenancy.tenant_packages.update'
                UNION ALL SELECT 'tenancy.tenant_packages.disable'
            ) AS actionCodes
        ) AS distinctMapped
        GROUP BY distinctMapped.Id, distinctMapped.elem
    ) AS mapped
        ON mapped.Id = source.Id
    WHERE source.PermissionsJson LIKE '%tenancy.tenant_packages.write%'
    GROUP BY source.Id
) AS rebuilt ON rebuilt.Id = apiKey.Id
SET apiKey.PermissionsJson = rebuilt.PermissionsJson
WHERE apiKey.PermissionsJson LIKE '%tenancy.tenant_packages.write%';