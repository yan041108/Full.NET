-- 人工向量沿用既有批次、账本及清理表，仅增加独立来源协议；不重写既有向量或代次状态。
-- 先验证覆盖全部既有格式的新约束，再删除旧约束，支持各 DDL 提交点未记账重跑。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_CorrectionFormats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_CorrectionFormats CHECK (
        Dimensions IN (1536,3072) AND DistanceKey='cosine' AND DATALENGTH(DistanceKey)=6 AND DATALENGTH(Payload) BETWEEN 8 AND 524288 AND (
            (FormatKey='knowledge-vector-batch-v1' AND DATALENGTH(FormatKey)=25)
            OR (FormatKey='knowledge-pdf-vector-batch-v1' AND DATALENGTH(FormatKey)=29)
            OR (FormatKey='knowledge-docx-vector-batch-v1' AND DATALENGTH(FormatKey)=30)
            OR (FormatKey='knowledge-document-vector-batch-v1' AND DATALENGTH(FormatKey)=34)
            OR (FormatKey='knowledge-correction-vector-v1' AND DATALENGTH(FormatKey)=30)));
GO
ALTER TABLE dbo.fn_ai_knowledge_index_vector WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_index_vector_CorrectionFormats;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_CorrectionFormats' AND is_disabled=0 AND is_not_trusted=0)
    AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_DocumentFormats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector DROP CONSTRAINT CK_fn_ai_knowledge_index_vector_DocumentFormats;
GO
