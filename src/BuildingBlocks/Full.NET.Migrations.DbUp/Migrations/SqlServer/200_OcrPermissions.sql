-- 200：为全部存量角色补齐 OCR Provider 与身份证识别权限。

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM dbo.fn_identity_role AS roles
CROSS JOIN (
    VALUES
        (N'ocr.providers.read'),
        (N'ocr.providers.update'),
        (N'ocr.providers.test'),
        (N'ocr.id_card_tasks.read'),
        (N'ocr.id_card_tasks.create'),
        (N'ocr.id_card_tasks.confirm'),
        (N'ocr.id_card_tasks.reject')
) AS actions(PermissionCode)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
