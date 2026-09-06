-- 131：为 Host 文件虚拟目录、元数据更新与引用查询补齐可分配权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, actions.PermissionCode
FROM dbo.fn_identity_role_permission AS legacy
CROSS JOIN (
    VALUES
        (N'files.files.update'),
        (N'files.file_references.read'),
        (N'files.folders.create'),
        (N'files.folders.update'),
        (N'files.folders.delete')
) AS actions(PermissionCode)
WHERE legacy.PermissionCode = N'files.files.read'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = actions.PermissionCode
  );

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT legacy.RoleId, N'files.files.update'
FROM dbo.fn_identity_role_permission AS legacy
WHERE legacy.PermissionCode = N'files.files.upload'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = legacy.RoleId
      AND existing.PermissionCode = N'files.files.update'
  );

UPDATE dbo.fn_identity_api_key
SET PermissionsJson = rebuilt.Json
FROM dbo.fn_identity_api_key AS apiKey
CROSS APPLY (
    SELECT
        CASE
            WHEN COUNT(*) = 0 THEN N'[]'
            ELSE N'[' + STRING_AGG(quoted.Value, N',') WITHIN GROUP (ORDER BY quoted.SortKey) + N']'
        END AS Json
    FROM (
        SELECT DISTINCT
            permissions.PermissionCode AS SortKey,
            N'"' + STRING_ESCAPE(permissions.PermissionCode, N'json') + N'"' AS Value
        FROM (
            SELECT CAST(element.value AS nvarchar(128)) AS PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS element
            UNION ALL
            SELECT actions.PermissionCode
            FROM OPENJSON(apiKey.PermissionsJson) AS readProbe
            CROSS JOIN (
                VALUES
                    (N'files.files.update'),
                    (N'files.file_references.read'),
                    (N'files.folders.create'),
                    (N'files.folders.update'),
                    (N'files.folders.delete')
            ) AS actions(PermissionCode)
            WHERE readProbe.value = N'files.files.read'
            UNION ALL
            SELECT N'files.files.update'
            FROM OPENJSON(apiKey.PermissionsJson) AS uploadProbe
            WHERE uploadProbe.value = N'files.files.upload'
              AND NOT EXISTS (
                SELECT 1
                FROM OPENJSON(apiKey.PermissionsJson) AS updateProbe
                WHERE updateProbe.value = N'files.files.update'
              )
        ) AS permissions
    ) AS quoted
) AS rebuilt
WHERE apiKey.PermissionsJson LIKE N'%files.files.read%'
   OR apiKey.PermissionsJson LIKE N'%files.files.upload%';
