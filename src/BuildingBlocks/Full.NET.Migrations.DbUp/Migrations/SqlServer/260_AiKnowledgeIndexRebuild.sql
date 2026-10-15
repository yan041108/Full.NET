-- 260：保留旧代次和费用历史；启用重建前须完成全部 API/Worker 升级，回退保留兼容读取程序。
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_task', N'Generation') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD Generation int NOT NULL CONSTRAINT DF_fn_ai_knowledge_index_task_Generation DEFAULT (0) WITH VALUES;
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_task', N'RebuildRequestId') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD RebuildRequestId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.fn_ai_knowledge_index_task', N'RebuildPreviousTaskId') IS NULL
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD RebuildPreviousTaskId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'Generation','ColumnId'))
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD CONSTRAINT DF_fn_ai_knowledge_index_task_Generation DEFAULT (0) FOR Generation;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND name=N'CK_fn_ai_knowledge_index_task_Generation')
    ALTER TABLE dbo.fn_ai_knowledge_index_task WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_index_task_Generation CHECK
        ((Generation=0 AND RebuildRequestId IS NULL AND RebuildPreviousTaskId IS NULL)
        OR (Generation>0 AND RebuildRequestId IS NOT NULL AND RebuildPreviousTaskId IS NOT NULL AND RebuildPreviousTaskId<>Id));
GO
-- 新唯一约束先成立，再移除旧约束；未记账重放时不产生无来源唯一性的中间状态。
ALTER TABLE dbo.fn_ai_knowledge_index_task WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_index_task_Generation;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND name=N'UQ_fn_ai_knowledge_index_task_SourceGeneration')
    ALTER TABLE dbo.fn_ai_knowledge_index_task ADD CONSTRAINT UQ_fn_ai_knowledge_index_task_SourceGeneration UNIQUE (VersionId,DocumentVersion,KnowledgeBaseVersion,Generation);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND name=N'UX_fn_ai_knowledge_index_task_RebuildRequest')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_index_task_RebuildRequest ON dbo.fn_ai_knowledge_index_task (DocumentId,RebuildRequestId) WHERE RebuildRequestId IS NOT NULL;
GO
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND name=N'UQ_fn_ai_knowledge_index_task_Source')
    ALTER TABLE dbo.fn_ai_knowledge_index_task DROP CONSTRAINT UQ_fn_ai_knowledge_index_task_Source;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'Generation','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'同一来源快照内的单调重建代次；既有任务为零',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'Generation';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'RebuildRequestId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'文档内幂等重建请求键；不是任务主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'RebuildRequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'),N'RebuildPreviousTaskId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'显式重建时预期的上一任务；保留原任务与费用历史',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_task',@level2type=N'COLUMN',@level2name=N'RebuildPreviousTaskId';
GO
