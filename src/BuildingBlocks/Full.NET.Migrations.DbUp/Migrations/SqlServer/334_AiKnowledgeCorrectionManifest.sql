-- 完整人工计划与自动计划共享一任务一清单、归档和清理边界，不改任务状态或旧载荷。
-- 先建立并信任覆盖全部既有格式的新守卫，再移除旧守卫；中断重跑不能留下无约束窗口。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest') AND name=N'CK_fn_ai_knowledge_document_manifest_Corrections')
    ALTER TABLE dbo.fn_ai_knowledge_document_manifest WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_document_manifest_Corrections CHECK (
        (FormatKey='knowledge-document-manifest-v1' AND DATALENGTH(FormatKey)=30 AND DATALENGTH(Payload) BETWEEN 8 AND 16777215)
        OR (FormatKey='knowledge-docx-manifest-v1' AND DATALENGTH(FormatKey)=26 AND DATALENGTH(Payload) BETWEEN 8 AND 8388608)
        OR (FormatKey='knowledge-correction-manifest-v1' AND DATALENGTH(FormatKey)=32 AND DATALENGTH(Payload) BETWEEN 8 AND 16777215));
GO
ALTER TABLE dbo.fn_ai_knowledge_document_manifest WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_document_manifest_Corrections;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest') AND name=N'CK_fn_ai_knowledge_document_manifest_Corrections' AND is_disabled=0 AND is_not_trusted=0)
    AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest') AND name=N'CK_fn_ai_knowledge_document_manifest_Bounds')
    ALTER TABLE dbo.fn_ai_knowledge_document_manifest DROP CONSTRAINT CK_fn_ai_knowledge_document_manifest_Bounds;
GO
