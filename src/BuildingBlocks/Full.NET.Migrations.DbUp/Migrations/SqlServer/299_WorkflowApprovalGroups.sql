-- 299：组内席位与组间门槛分别持久化，旧步骤不伪造分组历史。
IF OBJECT_ID(N'dbo.fn_workflow_approval_group',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_approval_group (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NOT NULL,
        GroupKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        GroupName nvarchar(128) NULL,
        SequenceNo int NOT NULL,
        ModeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequiredApprovalCount int NOT NULL,
        SlotCount int NOT NULL,
        StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Revision bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT pk_fn_workflow_approval_group PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_approval_group_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_approval_group_step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step(Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流独立审批组事实', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group';
GO
IF COL_LENGTH(N'dbo.fn_workflow_step',N'ApprovalGroupCount') IS NULL
    ALTER TABLE dbo.fn_workflow_step ADD ApprovalGroupCount int NULL;
GO
IF COL_LENGTH(N'dbo.fn_workflow_approval_slot',N'GroupId') IS NULL
    ALTER TABLE dbo.fn_workflow_approval_slot ADD GroupId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND name=N'uq_fn_workflow_approval_group_key')
    CREATE UNIQUE INDEX uq_fn_workflow_approval_group_key ON dbo.fn_workflow_approval_group (StepId, GroupKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND name=N'uq_fn_workflow_approval_group_sequence')
    CREATE UNIQUE INDEX uq_fn_workflow_approval_group_sequence ON dbo.fn_workflow_approval_group (StepId, SequenceNo);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND name=N'ix_fn_workflow_approval_group_instance_status')
    CREATE INDEX ix_fn_workflow_approval_group_instance_status ON dbo.fn_workflow_approval_group (InstanceId, StatusKey, StepId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name=N'ix_fn_workflow_approval_slot_group_decision')
    CREATE INDEX ix_fn_workflow_approval_slot_group_decision ON dbo.fn_workflow_approval_slot(GroupId, DecisionKey, SequenceNo);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name=N'fk_fn_workflow_approval_slot_group')
    ALTER TABLE dbo.fn_workflow_approval_slot WITH CHECK ADD CONSTRAINT fk_fn_workflow_approval_slot_group FOREIGN KEY(GroupId) REFERENCES dbo.fn_workflow_approval_group(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND name=N'ck_fn_workflow_approval_group_state')
    ALTER TABLE dbo.fn_workflow_approval_group WITH CHECK ADD CONSTRAINT ck_fn_workflow_approval_group_state CHECK (SequenceNo BETWEEN 1 AND 8 AND SlotCount BETWEEN 1 AND 20 AND RequiredApprovalCount BETWEEN 1 AND SlotCount AND Revision>0 AND ModeKey IN ('single','all','any','nOfM','percent','sequential') AND (ModeKey<>'single' OR SlotCount=1) AND (ModeKey NOT IN ('all','sequential') OR RequiredApprovalCount=SlotCount) AND (ModeKey<>'any' OR RequiredApprovalCount=1) AND ((StatusKey='active' AND CompletedAtUtc IS NULL) OR (StatusKey IN ('approved','rejected','cancelled') AND CompletedAtUtc IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'ck_fn_workflow_step_approval_groups')
    ALTER TABLE dbo.fn_workflow_step WITH CHECK ADD CONSTRAINT ck_fn_workflow_step_approval_groups CHECK ((ApprovalGroupCount IS NULL AND (ApprovalModeKey IS NULL OR ApprovalModeKey<>'group')) OR (ApprovalGroupCount IS NOT NULL AND ApprovalModeKey IS NOT NULL AND ApprovalModeKey='group' AND ApprovalGroupCount BETWEEN 2 AND 8 AND RequiredApprovalCount IS NOT NULL AND RequiredApprovalCount BETWEEN 1 AND ApprovalGroupCount AND ApprovalSlotCount IS NOT NULL AND ApprovalSlotCount BETWEEN ApprovalGroupCount AND 20));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'StepId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属分组审批步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'StepId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'GroupKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布版本固定组键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'GroupKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'GroupName','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布版本固定组名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'GroupName';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'SequenceNo','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布版本固定组顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'SequenceNo';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'ModeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定组内审批模式', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'ModeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'RequiredApprovalCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定组内同意门槛', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'RequiredApprovalCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'SlotCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次实际组内人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'SlotCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'StatusKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组内收敛状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组状态乐观锁修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组激活时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_group'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组收敛时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_group', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
GO

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'),N'ApprovalGroupCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次实际审批组数；旧步骤为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'ApprovalGroupCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'),N'GroupId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次审批组标识；普通席位为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'GroupId';
GO
