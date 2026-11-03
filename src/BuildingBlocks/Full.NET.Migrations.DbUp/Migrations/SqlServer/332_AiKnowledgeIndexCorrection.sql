-- 冻结人工索引输入，不从新的修订头恢复旧任务；既有自动任务保持空身份。
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_task',N'CorrectionSourceId') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD CorrectionSourceId uniqueidentifier NULL;
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_task',N'CorrectionRevisionId') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD CorrectionRevisionId uniqueidentifier NULL;
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_task',N'CorrectionHeadVersion') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD CorrectionHeadVersion int NULL;
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_task',N'CorrectionSourceHash') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD CorrectionSourceHash varchar(64) NULL;
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_task',N'CorrectionPayloadHash') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD CorrectionPayloadHash varchar(64) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND name=N'CK_fn_ai_knowledge_index_task_Correction')
    ALTER TABLE dbo.fn_ai_knowledge_index_task WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_index_task_Correction CHECK ((CorrectionSourceId IS NULL AND CorrectionRevisionId IS NULL AND CorrectionHeadVersion IS NULL AND CorrectionSourceHash IS NULL AND CorrectionPayloadHash IS NULL) OR (CorrectionSourceId IS NOT NULL AND CorrectionRevisionId IS NOT NULL AND CorrectionHeadVersion IS NOT NULL AND CorrectionHeadVersion>0 AND CorrectionSourceHash IS NOT NULL AND CorrectionPayloadHash IS NOT NULL AND LEN(CorrectionSourceHash)=64 AND LEN(CorrectionPayloadHash)=64));
GO
ALTER TABLE dbo.fn_ai_knowledge_index_task WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_index_task_Correction;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'CorrectionSourceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结的人工修订原解析 UUID，自动解析为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'CorrectionSourceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'CorrectionRevisionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结的人工修订 UUID，自动解析为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'CorrectionRevisionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'CorrectionHeadVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'入队时冻结的人工修订头版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'CorrectionHeadVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'CorrectionSourceHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结的原解析及运行时共同指纹',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'CorrectionSourceHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'CorrectionPayloadHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结的规范人工修订载荷摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'CorrectionPayloadHash';
GO
