-- 手动催办固定请求、真实收件人数量和步骤预算；不冒充办理动作或自动超时信号。
IF OBJECT_ID(N'dbo.fn_workflow_manual_reminder',N'U') IS NULL
BEGIN
CREATE TABLE dbo.fn_workflow_manual_reminder (
    Id uniqueidentifier NOT NULL,
    InstanceId uniqueidentifier NOT NULL,
    StepId uniqueidentifier NOT NULL,
    RequestedByUserId uniqueidentifier NOT NULL,
    InstanceRevision bigint NOT NULL,
    StepRevision bigint NOT NULL,
    ManualSequence int NOT NULL,
    RecipientCount int NOT NULL,
    IdempotencyKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_fn_workflow_manual_reminder PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT fk_fn_workflow_manual_reminder_action FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_action_record(Id),
    CONSTRAINT fk_fn_workflow_manual_reminder_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_manual_reminder_step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step(Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流手动催办请求与首次回执；独立于自动SLA提醒预算', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'催办请求及实际动作标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'StepId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际活动步骤标识，交接不能重置催办预算', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'StepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'RequestedByUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际发起催办的流程发起人', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'RequestedByUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'InstanceRevision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受理时的实例修订号，知会不推进流程', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'InstanceRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'StepRevision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受理时的实际步骤修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'StepRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'ManualSequence','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'同一步骤从一开始的独立人工催办序号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'ManualSequence';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'RecipientCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次真实活动待办收件人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'RecipientCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'IdempotencyKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例内固定请求身份', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规范请求的SHA256摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_manual_reminder'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'服务端UTC受理时间，不修改自动SLA计数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_manual_reminder', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND name=N'ck_fn_workflow_manual_reminder_bounds')
    ALTER TABLE dbo.fn_workflow_manual_reminder ADD CONSTRAINT ck_fn_workflow_manual_reminder_bounds CHECK (InstanceRevision>=1 AND StepRevision>=1 AND ManualSequence BETWEEN 1 AND 20 AND RecipientCount BETWEEN 1 AND 20);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND name=N'ck_fn_workflow_manual_reminder_request')
    ALTER TABLE dbo.fn_workflow_manual_reminder ADD CONSTRAINT ck_fn_workflow_manual_reminder_request CHECK (LEN(LTRIM(RTRIM(IdempotencyKey))) BETWEEN 1 AND 128 AND LEN(RequestHash)=64);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND name=N'uq_fn_workflow_manual_reminder_request')
    CREATE UNIQUE INDEX uq_fn_workflow_manual_reminder_request ON dbo.fn_workflow_manual_reminder(InstanceId, IdempotencyKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND name=N'uq_fn_workflow_manual_reminder_sequence')
    CREATE UNIQUE INDEX uq_fn_workflow_manual_reminder_sequence ON dbo.fn_workflow_manual_reminder(StepId, ManualSequence);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_manual_reminder') AND name=N'ix_fn_workflow_manual_reminder_step_time')
    CREATE INDEX ix_fn_workflow_manual_reminder_step_time ON dbo.fn_workflow_manual_reminder(InstanceId, StepId, CreatedAtUtc);
GO
