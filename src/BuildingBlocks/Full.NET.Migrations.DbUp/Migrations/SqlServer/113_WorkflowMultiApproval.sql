-- 多人审批采用追加式席位事实；步骤快照列保持可空，以支持旧 API 滚动升级和存量单人步骤。
IF COL_LENGTH(N'dbo.fn_workflow_step', N'ApprovalModeKey') IS NULL
    ALTER TABLE dbo.fn_workflow_step ADD ApprovalModeKey varchar(16) NULL;
IF COL_LENGTH(N'dbo.fn_workflow_step', N'RequiredApprovalCount') IS NULL
    ALTER TABLE dbo.fn_workflow_step ADD RequiredApprovalCount int NULL;
IF COL_LENGTH(N'dbo.fn_workflow_step', N'ApprovalSlotCount') IS NULL
    ALTER TABLE dbo.fn_workflow_step ADD ApprovalSlotCount int NULL;
IF COL_LENGTH(N'dbo.fn_workflow_action_record', N'ResultStatusKey') IS NULL
    ALTER TABLE dbo.fn_workflow_action_record ADD ResultStatusKey varchar(16) NULL;
IF COL_LENGTH(N'dbo.fn_workflow_action_record', N'ResultTodoId') IS NULL
    ALTER TABLE dbo.fn_workflow_action_record ADD ResultTodoId uniqueidentifier NULL;
GO

IF OBJECT_ID(N'dbo.fn_workflow_approval_slot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_approval_slot
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NOT NULL,
        TodoId uniqueidentifier NOT NULL,
        AssigneeUserId uniqueidentifier NOT NULL,
        DecisionKey varchar(16) NULL,
        Revision bigint NOT NULL CONSTRAINT DF_fn_workflow_approval_slot_Revision DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        DecidedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_workflow_approval_slot PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_approval_slot_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_approval_slot_Step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step(Id),
        CONSTRAINT FK_fn_workflow_approval_slot_Todo FOREIGN KEY (TodoId) REFERENCES dbo.fn_workflow_todo(Id),
        CONSTRAINT UQ_fn_workflow_approval_slot_Step_Assignee UNIQUE (StepId, AssigneeUserId),
        CONSTRAINT UQ_fn_workflow_approval_slot_Todo UNIQUE (TodoId),
        CONSTRAINT CK_fn_workflow_approval_slot_Revision CHECK (Revision > 0),
        CONSTRAINT CK_fn_workflow_approval_slot_Decision CHECK (DecisionKey IS NULL OR DecisionKey IN ('approve', 'reject', 'cancelled'))
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot')
      AND name = N'IX_fn_workflow_approval_slot_Step_Decision')
    CREATE INDEX IX_fn_workflow_approval_slot_Step_Decision
        ON dbo.fn_workflow_approval_slot(StepId, DecisionKey, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = 0
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流多人审批席位表',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'InstanceId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'InstanceId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'StepId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审批步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'StepId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'TodoId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'一对一待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'TodoId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'AssigneeUserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'席位办理人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'AssigneeUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'DecisionKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审批决定机器键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'DecisionKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'Revision', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'席位修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'Revision';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'席位创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'), N'DecidedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'决定提交时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'DecidedAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_step') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'ApprovalModeKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审批模式键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'ApprovalModeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_step') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'RequiredApprovalCount', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'法定同意票数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'RequiredApprovalCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_step') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'ApprovalSlotCount', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审批席位总数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'ApprovalSlotCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'ResultStatusKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'动作确定性结果状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'ResultStatusKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'ResultTodoId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'动作确定性结果待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'ResultTodoId';
