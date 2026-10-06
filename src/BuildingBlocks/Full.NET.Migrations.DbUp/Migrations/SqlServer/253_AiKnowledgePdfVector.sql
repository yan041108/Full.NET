-- 253：原批次主键/外键不变，扩展独立 PDF 向量格式，不重写载荷、账本或 ready。
-- SQL Server WITH CHECK 校验历史行并信任新约束后，才移除旧文本守卫；需要迁移窗口的 schema 锁。
-- 二进制排序规则仍忽略尾空格，格式和 cosine 同时校验字节长度；非法旧行失败关闭，须显式修复后前滚。
-- 应用可回退读取旧文本；有 PDF 行时不能直接收缩约束，本脚本不提供自动降级或数据删除。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_Formats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_Formats CHECK (Dimensions IN (1536,3072) AND ((FormatKey='knowledge-vector-batch-v1' AND DATALENGTH(FormatKey)=25) OR (FormatKey='knowledge-pdf-vector-batch-v1' AND DATALENGTH(FormatKey)=29)) AND DistanceKey='cosine' AND DATALENGTH(DistanceKey)=6 AND DATALENGTH(Payload) BETWEEN 8 AND 524288);
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_Formats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_index_vector_Formats;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_Formats' AND is_disabled=0 AND is_not_trusted=0)
    AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_Bounds')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector DROP CONSTRAINT CK_fn_ai_knowledge_index_vector_Bounds;
GO
-- 每个元数据步骤独立恢复，覆盖 DDL 成功但 DbUp 尚未记账的中断点。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'知识库已结算文本或PDF向量批次派生数据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector';
ELSE
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'知识库已结算文本或PDF向量批次派生数据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'Payload','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'完整有界文本或PDF分页片段与小端向量载荷',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector',@level2type=N'COLUMN',@level2name=N'Payload';
ELSE
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'完整有界文本或PDF分页片段与小端向量载荷',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector',@level2type=N'COLUMN',@level2name=N'Payload';
GO
