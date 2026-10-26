-- 289：签名证据和 Workflow 动作同事务提交，Files claim 预留与确认在事务外。
IF OBJECT_ID(N'dbo.fn_workflow_action_evidence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_action_evidence (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NOT NULL,
        TodoId uniqueidentifier NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        OriginalAssigneeUserId uniqueidentifier NOT NULL,
        ActionKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FormVersionId uniqueidentifier NOT NULL,
        FormRevision bigint NOT NULL,
        SubmissionHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FileId uniqueidentifier NOT NULL,
        FileHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT pk_fn_workflow_action_evidence PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT uq_fn_workflow_action_evidence_request UNIQUE (InstanceId, IdempotencyKey),
        CONSTRAINT fk_fn_workflow_action_evidence_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance (Id),
        CONSTRAINT fk_fn_workflow_action_evidence_step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step (Id),
        CONSTRAINT fk_fn_workflow_action_evidence_todo FOREIGN KEY (TodoId) REFERENCES dbo.fn_workflow_todo (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'Id', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'InstanceId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'StepId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'活动步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'StepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'TodoId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际办理待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'TodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'TenantScopeKey', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'ActorUserId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际执行人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'OriginalAssigneeUserId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原办理人标识快照', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'OriginalAssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'ActionKey', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'稳定办理动作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'ActionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'IdempotencyKey', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'动作幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'FormVersionId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定表单版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'FormVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'FormRevision', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'签署提交表单修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'FormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'SubmissionHash', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'签署提交内容摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'SubmissionHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'FileId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'签名文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'FileId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'FileHash', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际签名文件内容摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'FileHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_evidence'), N'CreatedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'证据创建时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_action_evidence') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流动作签名证据', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_evidence';
