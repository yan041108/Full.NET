-- 预览引用、尝试与删除事实分别持久化；每步结构探测支持未记账的半完成重跑。
IF OBJECT_ID('dbo.fn_document_preview_reference', 'U') IS NULL
    CREATE TABLE dbo.fn_document_preview_reference (
        Id uniqueidentifier NOT NULL,
        DocumentId uniqueidentifier NOT NULL,
        TaskId uniqueidentifier NOT NULL,
        FileId uniqueidentifier NOT NULL,
        Purpose varchar(16) NOT NULL,
        StateKey varchar(24) NOT NULL,
        ReleaseAfterUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_document_preview_reference PRIMARY KEY NONCLUSTERED (Id)
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = 0 AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档预览源及输出的独立引用意图', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference';
GO
IF OBJECT_ID('dbo.fn_document_preview_attempt', 'U') IS NULL
    CREATE TABLE dbo.fn_document_preview_attempt (
        Id uniqueidentifier NOT NULL,
        TaskId uniqueidentifier NOT NULL,
        ClaimVersion bigint NOT NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        ErrorCode varchar(128) NULL,
        CONSTRAINT PK_fn_document_preview_attempt PRIMARY KEY NONCLUSTERED (Id)
    );
GO
IF OBJECT_ID('dbo.fn_document_deletion_fact', 'U') IS NULL
    CREATE TABLE dbo.fn_document_deletion_fact (
        Id uniqueidentifier NOT NULL,
        DocumentId uniqueidentifier NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        RetainUntilUtc datetimeoffset(7) NOT NULL,
        ReasonKey varchar(32) NOT NULL,
        VersionCount bigint NOT NULL,
        PreviewTaskCount bigint NOT NULL,
        CONSTRAINT PK_fn_document_deletion_fact PRIMARY KEY NONCLUSTERED (Id)
    );
GO
IF COL_LENGTH('dbo.fn_document_preview_task', 'SourceReferenceId') IS NULL
    ALTER TABLE dbo.fn_document_preview_task ADD SourceReferenceId uniqueidentifier NULL;
GO
IF COL_LENGTH('dbo.fn_document_preview_task', 'OutputReferenceId') IS NULL
    ALTER TABLE dbo.fn_document_preview_task ADD OutputReferenceId uniqueidentifier NULL;
GO
IF COL_LENGTH('dbo.fn_document_preview_task', 'OutputOwnerId') IS NULL
    ALTER TABLE dbo.fn_document_preview_task ADD OutputOwnerId uniqueidentifier NULL;
GO
IF COL_LENGTH('dbo.fn_document_preview_task', 'AttemptCount') IS NULL
    ALTER TABLE dbo.fn_document_preview_task ADD AttemptCount int NOT NULL DEFAULT 0;
GO
IF COL_LENGTH('dbo.fn_document_preview_task', 'NextAttemptAtUtc') IS NULL
    ALTER TABLE dbo.fn_document_preview_task ADD NextAttemptAtUtc datetimeoffset(7) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_preview_reference') AND name = 'UX_fn_document_preview_reference_TaskId')
    CREATE UNIQUE INDEX UX_fn_document_preview_reference_TaskId ON dbo.fn_document_preview_reference (TaskId, Purpose);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_preview_reference') AND name = 'IX_fn_document_preview_reference_FileId')
    CREATE INDEX IX_fn_document_preview_reference_FileId ON dbo.fn_document_preview_reference (FileId, StateKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_preview_attempt') AND name = 'UX_fn_document_preview_attempt_TaskId')
    CREATE UNIQUE INDEX UX_fn_document_preview_attempt_TaskId ON dbo.fn_document_preview_attempt (TaskId, ClaimVersion);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND name = 'UX_fn_document_deletion_fact_DocumentId')
    CREATE UNIQUE INDEX UX_fn_document_deletion_fact_DocumentId ON dbo.fn_document_deletion_fact (DocumentId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND name = 'IX_fn_document_deletion_fact_RetainUntilUtc')
    CREATE INDEX IX_fn_document_deletion_fact_RetainUntilUtc ON dbo.fn_document_deletion_fact (RetainUntilUtc, Id);
GO
UPDATE fn_document_preview_task SET StatusKey = 'failed', ErrorCode = 'document.preview.lifecycle_upgrade_required' WHERE SourceReferenceId IS NULL AND StatusKey IN ('pending', 'processing');
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_reference'), 'Id', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可复用的应用 UUID v7 引用标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_reference'), 'DocumentId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标识，无级联关系', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_reference'), 'TaskId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预览任务标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference', @level2type=N'COLUMN', @level2name=N'TaskId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_reference'), 'FileId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源或预留输出文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference', @level2type=N'COLUMN', @level2name=N'FileId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_reference'), 'Purpose', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'source 或 output', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference', @level2type=N'COLUMN', @level2name=N'Purpose';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_reference'), 'StateKey', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'preparing claimed 或 released', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference', @level2type=N'COLUMN', @level2name=N'StateKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_reference'), 'ReleaseAfterUtc', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'有效租约保护截止时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference', @level2type=N'COLUMN', @level2name=N'ReleaseAfterUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_reference') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_reference'), 'CreatedAtUtc', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'意图创建时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_reference', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_attempt') AND minor_id = 0 AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档预览领取尝试事实', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_attempt';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_attempt') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_attempt'), 'Id', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用 UUID v7 尝试标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_attempt', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_attempt') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_attempt'), 'TaskId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预览任务标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_attempt', @level2type=N'COLUMN', @level2name=N'TaskId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_attempt') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_attempt'), 'ClaimVersion', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'领取版本，用于拒绝迟到执行者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_attempt', @level2type=N'COLUMN', @level2name=N'ClaimVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_attempt') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_attempt'), 'StartedAtUtc', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次领取时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_attempt', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_attempt') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_attempt'), 'CompletedAtUtc', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次结束时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_attempt', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_attempt') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_attempt'), 'ErrorCode', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'稳定终止原因', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_attempt', @level2type=N'COLUMN', @level2name=N'ErrorCode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = 0 AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档永久删除最小事实，独立于父文档保留', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_deletion_fact'), 'Id', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用 UUID v7 审计标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_deletion_fact'), 'DocumentId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'被删除资源标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_deletion_fact'), 'ActorUserId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'永久删除操作者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact', @level2type=N'COLUMN', @level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_deletion_fact'), 'OccurredAtUtc', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始永久删除时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_deletion_fact'), 'RetainUntilUtc', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最小事实保留截止时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact', @level2type=N'COLUMN', @level2name=N'RetainUntilUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_deletion_fact'), 'ReasonKey', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除原因机器码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact', @level2type=N'COLUMN', @level2name=N'ReasonKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_deletion_fact'), 'VersionCount', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始删除时版本数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact', @level2type=N'COLUMN', @level2name=N'VersionCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_deletion_fact') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_deletion_fact'), 'PreviewTaskCount', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始删除时预览任务数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_deletion_fact', @level2type=N'COLUMN', @level2name=N'PreviewTaskCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_task'), 'SourceReferenceId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源文件独立引用意图', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'SourceReferenceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_task'), 'OutputReferenceId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输出文件独立引用意图', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'OutputReferenceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_task'), 'OutputOwnerId', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定的文档创建者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'OutputOwnerId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_task'), 'AttemptCount', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已领取次数，含崩溃重领', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'AttemptCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_task'), 'NextAttemptAtUtc', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'失败退避后可领取时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'NextAttemptAtUtc';
GO
-- 新增准备和取消状态；独立探测支持约束删除后中断的恢复。
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.fn_document_preview_task') AND name = 'CK_fn_document_preview_task_StatusKey' AND definition NOT LIKE '%preparing%')
    ALTER TABLE dbo.fn_document_preview_task DROP CONSTRAINT CK_fn_document_preview_task_StatusKey;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.fn_document_preview_task') AND name = 'CK_fn_document_preview_task_StatusKey')
    ALTER TABLE dbo.fn_document_preview_task ADD CONSTRAINT CK_fn_document_preview_task_StatusKey CHECK (StatusKey IN ('preparing','pending','processing','succeeded','failed','cancelled'));
GO
IF COL_LENGTH('dbo.fn_document_preview_task', 'LeaseExpiresAtUtc') IS NULL
    ALTER TABLE dbo.fn_document_preview_task ADD LeaseExpiresAtUtc datetimeoffset(7) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_preview_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_preview_task'), 'LeaseExpiresAtUtc', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'领取时固定的租约截止，热配置不提前回收', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
GO
