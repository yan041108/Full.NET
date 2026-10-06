-- 252：保持一个任务一份清单，扩展独立 PDF 格式，不重写正文、回填任务或推进 ready。
-- 新约束先校验并启用，再移除旧约束；DDL 需迁移窗口的 schema 锁，应用回退可继续读取文本。
-- 不自动收缩格式；回退约束前必须先处理现存 PDF 行，恢复优先前滚重放本脚本。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND name=N'CK_fn_ai_knowledge_text_manifest_Formats')
    ALTER TABLE dbo.fn_ai_knowledge_text_manifest WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_text_manifest_Formats CHECK (((FormatKey='knowledge-text-manifest-v1' AND DATALENGTH(FormatKey)=26) OR (FormatKey='knowledge-pdf-manifest-v1' AND DATALENGTH(FormatKey)=25)) AND DATALENGTH(Payload) BETWEEN 8 AND 131072);
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND name=N'CK_fn_ai_knowledge_text_manifest_Formats')
    ALTER TABLE dbo.fn_ai_knowledge_text_manifest WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_text_manifest_Formats;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND name=N'CK_fn_ai_knowledge_text_manifest_Formats' AND is_disabled=0 AND is_not_trusted=0)
    AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND name=N'CK_fn_ai_knowledge_text_manifest_Bounds')
    ALTER TABLE dbo.fn_ai_knowledge_text_manifest DROP CONSTRAINT CK_fn_ai_knowledge_text_manifest_Bounds;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'知识库索引任务不可替换的完整文本或PDF计划',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_text_manifest';
ELSE
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'知识库索引任务不可替换的完整文本或PDF计划',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_text_manifest';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest'),N'Payload','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不含正文的完整有界文本或PDF批次清单',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_text_manifest',@level2type=N'COLUMN',@level2name=N'Payload';
ELSE
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'不含正文的完整有界文本或PDF批次清单',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_text_manifest',@level2type=N'COLUMN',@level2name=N'Payload';
GO
