-- 255：一个任务一个完整清单；不回填旧任务、不改 ready，仅建立 AI 模块内外键。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_docx_manifest (
        Id uniqueidentifier NOT NULL,
        FormatKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PayloadHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Payload varbinary(max) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_docx_manifest PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_docx_manifest_Id FOREIGN KEY (Id) REFERENCES dbo.fn_ai_knowledge_index_task (Id),
        CONSTRAINT CK_fn_ai_knowledge_docx_manifest_Bounds CHECK (FormatKey='knowledge-docx-manifest-v1' AND DATALENGTH(FormatKey)=26 AND DATALENGTH(Payload) BETWEEN 8 AND 8388608)
    );
END;
GO
-- 说明独立探测，允许建表成功但 DbUp 未记账时继续恢复，不触碰原载荷。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库索引任务不可替换的完整 DOCX 计划', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_docx_manifest';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键及所属索引任务标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_docx_manifest', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest'),N'FormatKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定清单格式版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_docx_manifest', @level2type=N'COLUMN', @level2name=N'FormatKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest'),N'PayloadHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整清单载荷 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_docx_manifest', @level2type=N'COLUMN', @level2name=N'PayloadHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest'),N'Payload','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'包含完整正文及逻辑块坐标的有界 DOCX 计划', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_docx_manifest', @level2type=N'COLUMN', @level2name=N'Payload';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_docx_manifest', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO

-- 255：原批次主键/外键不变，扩展独立 DOCX 向量格式，不重写载荷、账本或 ready。
-- SQL Server WITH CHECK 校验历史行并信任新约束后，才移除旧文本/PDF守卫；需要迁移窗口的 schema 锁。
-- 二进制排序规则仍忽略尾空格，格式和 cosine 同时校验字节长度；非法旧行失败关闭，须显式修复后前滚。
-- 应用可回退读取旧文本；有 DOCX 行时不能直接收缩约束，本脚本不提供自动降级或数据删除。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_DocxFormats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_DocxFormats CHECK (Dimensions IN (1536,3072) AND ((FormatKey='knowledge-vector-batch-v1' AND DATALENGTH(FormatKey)=25) OR (FormatKey='knowledge-pdf-vector-batch-v1' AND DATALENGTH(FormatKey)=29) OR (FormatKey='knowledge-docx-vector-batch-v1' AND DATALENGTH(FormatKey)=30)) AND DistanceKey='cosine' AND DATALENGTH(DistanceKey)=6 AND DATALENGTH(Payload) BETWEEN 8 AND 524288);
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_DocxFormats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_index_vector_DocxFormats;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_DocxFormats' AND is_disabled=0 AND is_not_trusted=0)
    AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_Formats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector DROP CONSTRAINT CK_fn_ai_knowledge_index_vector_Formats;
GO
-- 每个元数据步骤独立恢复，覆盖 DDL 成功但 DbUp 尚未记账的中断点。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'知识库已结算文本、PDF或DOCX向量批次派生数据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector';
ELSE
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'知识库已结算文本、PDF或DOCX向量批次派生数据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'Payload','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'有界文本片段、PDF分页或DOCX逻辑块及小端向量载荷',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector',@level2type=N'COLUMN',@level2name=N'Payload';
ELSE
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'有界文本片段、PDF分页或DOCX逻辑块及小端向量载荷',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector',@level2type=N'COLUMN',@level2name=N'Payload';
GO
