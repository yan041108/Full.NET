-- 200：为全部存量角色补齐 OCR Provider 与身份证识别权限。

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, actions.PermissionCode
FROM fn_identity_role AS roles
CROSS JOIN (
    SELECT 'ocr.providers.read' AS PermissionCode UNION ALL
    SELECT 'ocr.providers.update' UNION ALL
    SELECT 'ocr.providers.test' UNION ALL
    SELECT 'ocr.id_card_tasks.read' UNION ALL
    SELECT 'ocr.id_card_tasks.create' UNION ALL
    SELECT 'ocr.id_card_tasks.confirm' UNION ALL
    SELECT 'ocr.id_card_tasks.reject'
) AS actions
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = actions.PermissionCode
);
