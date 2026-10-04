-- 251：先扩展任务生命周期，再移除旧守卫；不自动完成旧任务或回填活动代次。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND name=N'CK_fn_ai_knowledge_index_task_Lifecycle')
    ALTER TABLE dbo.fn_ai_knowledge_index_task WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_index_task_Lifecycle CHECK (StateKey IN ('queued','running','failed','cancelled','completed'));
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND name=N'CK_fn_ai_knowledge_index_task_State')
    ALTER TABLE dbo.fn_ai_knowledge_index_task DROP CONSTRAINT CK_fn_ai_knowledge_index_task_State;
GO
IF OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_active_generation (
        Id uniqueidentifier NOT NULL,
        DocumentId uniqueidentifier NOT NULL,
        IndexTaskId uniqueidentifier NOT NULL,
        VersionId uniqueidentifier NOT NULL,
        PlanHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ManifestHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ReceiptHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ReceiptJson varchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
        LeaseToken uniqueidentifier NOT NULL,
        ActivatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_active_generation PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_active_generation_DocumentId FOREIGN KEY (DocumentId) REFERENCES dbo.fn_ai_knowledge_document (Id),
        CONSTRAINT FK_fn_ai_knowledge_active_generation_IndexTaskId FOREIGN KEY (IndexTaskId) REFERENCES dbo.fn_ai_knowledge_index_task (Id),
        CONSTRAINT FK_fn_ai_knowledge_active_generation_VersionId FOREIGN KEY (VersionId) REFERENCES dbo.fn_ai_knowledge_version (Id),
        CONSTRAINT CK_fn_ai_knowledge_active_generation_Receipt CHECK (ISJSON(ReceiptJson)=1 AND DATALENGTH(ReceiptJson) BETWEEN 2 AND 1048576)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND name=N'UQ_fn_ai_knowledge_active_generation_DocumentId')
    CREATE UNIQUE INDEX UQ_fn_ai_knowledge_active_generation_DocumentId ON dbo.fn_ai_knowledge_active_generation (DocumentId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND name=N'UQ_fn_ai_knowledge_active_generation_IndexTaskId')
    CREATE UNIQUE INDEX UQ_fn_ai_knowledge_active_generation_IndexTaskId ON dbo.fn_ai_knowledge_active_generation (IndexTaskId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库文档当前完整活动代次及原完成回执', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'DocumentId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一所属文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'IndexTaskId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一已完成索引任务标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'IndexTaskId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'VersionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'来源版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'VersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'PlanHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整文本计划 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'PlanHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'ManifestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始清单字节 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'ManifestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'ReceiptHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整回执 JSON 字节 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'ReceiptHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'ReceiptJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'无正文的完整有界批次回执', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'ReceiptJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'LeaseToken','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原完成租约标识，仅用于内部重试', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'LeaseToken';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_active_generation'),N'ActivatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'激活时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_active_generation', @level2type=N'COLUMN', @level2name=N'ActivatedAtUtc';
GO
