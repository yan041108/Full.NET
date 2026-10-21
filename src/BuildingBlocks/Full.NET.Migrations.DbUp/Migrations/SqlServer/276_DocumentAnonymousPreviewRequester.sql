-- 匿名预览没有用户身份，不能以空 UUID 冒充已分配的用户标识。
ALTER TABLE dbo.fn_document_preview_task ALTER COLUMN RequestedByUserId uniqueidentifier NULL;
UPDATE dbo.fn_document_preview_task SET RequestedByUserId = NULL
WHERE RequestedByUserId = '00000000-0000-0000-0000-000000000000';
