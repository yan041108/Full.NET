-- 246：只追加索引任务；文件与模型仅保存标识，不建立跨模块关系，不提供 ready 状态。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_index_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_index_task (
    Id uniqueidentifier NOT NULL,
    DocumentId uniqueidentifier NOT NULL,
    VersionId uniqueidentifier NOT NULL,
    DocumentVersion int NOT NULL,
    KnowledgeBaseVersion int NOT NULL,
    ModelConfigId uniqueidentifier NOT NULL,
    ModelConfigVersion int NOT NULL,
    StateKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    AttemptCount int NOT NULL,
    FailureCode varchar(160) COLLATE Latin1_General_100_BIN2 NULL,
    LeaseToken uniqueidentifier NULL,
    LeaseExpiresAtUtc datetimeoffset(7) NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    UpdatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_ai_knowledge_index_task PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_fn_ai_knowledge_index_task_Document FOREIGN KEY (DocumentId) REFERENCES dbo.fn_ai_knowledge_document (Id),
    CONSTRAINT FK_fn_ai_knowledge_index_task_Version FOREIGN KEY (VersionId) REFERENCES dbo.fn_ai_knowledge_version (Id),
    CONSTRAINT UQ_fn_ai_knowledge_index_task_Source UNIQUE (VersionId, DocumentVersion, KnowledgeBaseVersion),
    CONSTRAINT CK_fn_ai_knowledge_index_task_Versions CHECK (DocumentVersion > 0 AND KnowledgeBaseVersion > 0 AND ModelConfigVersion > 0),
    CONSTRAINT CK_fn_ai_knowledge_index_task_Attempt CHECK (AttemptCount >= 0 AND AttemptCount <= 3),
    CONSTRAINT CK_fn_ai_knowledge_index_task_State CHECK (StateKey IN ('queued','running','failed','cancelled')),
    CONSTRAINT CK_fn_ai_knowledge_index_task_Lease CHECK ((StateKey = 'running' AND LeaseToken IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL) OR (StateKey <> 'running' AND LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL))
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND name = N'IX_fn_ai_knowledge_index_task_StateTime')
    CREATE INDEX IX_fn_ai_knowledge_index_task_StateTime ON dbo.fn_ai_knowledge_index_task (StateKey, LeaseExpiresAtUtc, CreatedAtUtc, Id);
GO
-- 建表、索引和每项注释可独立收敛，允许 DbUp 未记账时重放。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库索引任务及有界执行租约', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键及索引任务代次', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'DocumentId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'VersionId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可变来源文件版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'VersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'DocumentVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预期文档并发版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'DocumentVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'KnowledgeBaseVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预期目录与授权版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'KnowledgeBaseVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'ModelConfigId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获批向量模型配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'ModelConfigId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'ModelConfigVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获批精确模型配置版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'ModelConfigVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'StateKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'持久化索引阶段', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'StateKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'AttemptCount', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前人工重试周期内领取次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'AttemptCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'FailureCode', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'稳定失败码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'FailureCode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'LeaseToken', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前执行者租约令牌', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'LeaseToken';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'LeaseExpiresAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
GO
