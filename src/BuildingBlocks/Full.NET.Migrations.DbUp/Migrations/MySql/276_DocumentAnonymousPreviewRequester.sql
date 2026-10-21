-- 匿名预览没有用户身份，不能以空 UUID 冒充已分配的用户标识。
ALTER TABLE fn_document_preview_task MODIFY COLUMN RequestedByUserId BINARY(16) NULL COMMENT '请求人用户标识，匿名预览为空';
UPDATE fn_document_preview_task SET RequestedByUserId = NULL
WHERE RequestedByUserId = UNHEX('00000000000000000000000000000000');
