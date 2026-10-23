-- 277：统一结构清单；存量原字节、费用及活动指针不改写，旧 DOCX 代次须显式重建。
-- 各步骤按元数据恢复，不依赖 DbUp 已记账；新守卫验证成功后才移除原守卫。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest',N'U') IS NULL AND OBJECT_ID(N'dbo.fn_ai_knowledge_docx_manifest',N'U') IS NOT NULL
    EXEC sys.sp_rename N'dbo.fn_ai_knowledge_docx_manifest', N'fn_ai_knowledge_document_manifest', N'OBJECT';
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_import',N'ParsingPolicyKey') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_import ADD ParsingPolicyKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_fn_ai_knowledge_import_ParsingPolicyKey DEFAULT ('default');
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_import') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_import'),N'ParsingPolicyKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'导入时冻结的解析策略键，参与重试身份与索引计划摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_import',@level2type=N'COLUMN',@level2name=N'ParsingPolicyKey';
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_version',N'ParsingPolicyKey') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_version ADD ParsingPolicyKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_fn_ai_knowledge_version_ParsingPolicyKey DEFAULT ('default');
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_version'),N'ParsingPolicyKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'导入时冻结的解析策略键，参与重试身份与索引计划摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_version',@level2type=N'COLUMN',@level2name=N'ParsingPolicyKey';
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_vector',N'FormatKey')<64
    ALTER TABLE dbo.fn_ai_knowledge_index_vector ALTER COLUMN FormatKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest') AND name=N'CK_fn_ai_knowledge_document_manifest_Bounds')
    ALTER TABLE dbo.fn_ai_knowledge_document_manifest WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_document_manifest_Bounds CHECK (((FormatKey='knowledge-document-manifest-v1' AND DATALENGTH(FormatKey)=30 AND DATALENGTH(Payload) BETWEEN 8 AND 16777215) OR (FormatKey='knowledge-docx-manifest-v1' AND DATALENGTH(FormatKey)=26 AND DATALENGTH(Payload) BETWEEN 8 AND 8388608)));
ALTER TABLE dbo.fn_ai_knowledge_document_manifest WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_document_manifest_Bounds;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest') AND name=N'CK_fn_ai_knowledge_document_manifest_Bounds' AND is_disabled=0 AND is_not_trusted=0)
    AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest') AND name=N'CK_fn_ai_knowledge_docx_manifest_Bounds')
    ALTER TABLE dbo.fn_ai_knowledge_document_manifest DROP CONSTRAINT CK_fn_ai_knowledge_docx_manifest_Bounds;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_DocumentFormats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_DocumentFormats CHECK (Dimensions IN (1536,3072) AND ((FormatKey='knowledge-vector-batch-v1' AND DATALENGTH(FormatKey)=25) OR (FormatKey='knowledge-pdf-vector-batch-v1' AND DATALENGTH(FormatKey)=29) OR (FormatKey='knowledge-docx-vector-batch-v1' AND DATALENGTH(FormatKey)=30) OR (FormatKey='knowledge-document-vector-batch-v1' AND DATALENGTH(FormatKey)=34)) AND DistanceKey='cosine' AND DATALENGTH(DistanceKey)=6 AND DATALENGTH(Payload) BETWEEN 8 AND 524288);
ALTER TABLE dbo.fn_ai_knowledge_index_vector WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_index_vector_DocumentFormats;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_DocumentFormats' AND is_disabled=0 AND is_not_trusted=0)
    AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND name=N'CK_fn_ai_knowledge_index_vector_DocxFormats')
    ALTER TABLE dbo.fn_ai_knowledge_index_vector DROP CONSTRAINT CK_fn_ai_knowledge_index_vector_DocxFormats;
GO
IF OBJECT_ID(N'dbo.PK_fn_ai_knowledge_docx_manifest') IS NOT NULL AND OBJECT_ID(N'dbo.PK_fn_ai_knowledge_document_manifest') IS NULL
    EXEC sys.sp_rename N'dbo.PK_fn_ai_knowledge_docx_manifest',N'PK_fn_ai_knowledge_document_manifest',N'OBJECT';
GO
IF OBJECT_ID(N'dbo.FK_fn_ai_knowledge_docx_manifest_Id') IS NOT NULL AND OBJECT_ID(N'dbo.FK_fn_ai_knowledge_document_manifest_Id') IS NULL
    EXEC sys.sp_rename N'dbo.FK_fn_ai_knowledge_docx_manifest_Id',N'FK_fn_ai_knowledge_document_manifest_Id',N'OBJECT';
GO
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'知识库索引任务不可替换的完整多格式解析计划',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_document_manifest';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'知识库索引任务不可替换的完整多格式解析计划',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_document_manifest';
GO
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_manifest'),N'Payload','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'包含完整解析策略、正文及原生来源坐标的有界计划',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_document_manifest',@level2type=N'COLUMN',@level2name=N'Payload';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'包含完整解析策略、正文及原生来源坐标的有界计划',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_document_manifest',@level2type=N'COLUMN',@level2name=N'Payload';
GO
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'知识库已结算文本及多格式向量批次派生数据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'知识库已结算文本及多格式向量批次派生数据',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector';
GO
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'Payload','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'由完整清单绑定的有界来源区间及小端向量载荷',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector',@level2type=N'COLUMN',@level2name=N'Payload';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'由完整清单绑定的有界来源区间及小端向量载荷',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_vector',@level2type=N'COLUMN',@level2name=N'Payload';
GO
