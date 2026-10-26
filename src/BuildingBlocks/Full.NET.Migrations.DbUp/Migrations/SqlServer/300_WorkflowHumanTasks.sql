-- 300：办理提交与审批票独立持久化；依次办理保留未来成员而不伪造待办。
IF OBJECT_ID(N'dbo.fn_workflow_task',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_task (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        CompletionModeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        MemberCount int NOT NULL,
        RequiredCompletionCount int NOT NULL,
        StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Revision bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT pk_fn_workflow_task PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_task_instance FOREIGN KEY(InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_task_step FOREIGN KEY(Id) REFERENCES dbo.fn_workflow_step(Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流办理活动事实', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'办理步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'CompletionModeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定办理完成规则', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'CompletionModeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'MemberCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际固定办理人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'MemberCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'RequiredCompletionCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所需提交完成人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'RequiredCompletionCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'StatusKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'办理活动状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观锁修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'激活时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'收敛时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
GO
IF OBJECT_ID(N'dbo.fn_workflow_task_member',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_task_member (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        TaskId uniqueidentifier NOT NULL,
        TodoId uniqueidentifier NULL,
        AssigneeUserId uniqueidentifier NOT NULL,
        SequenceNo int NOT NULL,
        StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Revision bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT pk_fn_workflow_task_member PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_task_member_instance FOREIGN KEY(InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_task_member_task FOREIGN KEY(TaskId) REFERENCES dbo.fn_workflow_task(Id),
        CONSTRAINT fk_fn_workflow_task_member_todo FOREIGN KEY(TodoId) REFERENCES dbo.fn_workflow_todo(Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流办理成员及不可变待办绑定', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'办理成员标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'TaskId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属办理活动标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'TaskId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'TodoId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际激活待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'TodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'AssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定办理人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'AssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'SequenceNo','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定办理顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'SequenceNo';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'StatusKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'办理成员状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'成员乐观锁修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'成员冻结时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_task_member'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成或取消时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_task_member', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_task') AND name=N'ix_fn_workflow_task_instance_status')
    CREATE INDEX ix_fn_workflow_task_instance_status ON dbo.fn_workflow_task(InstanceId, StatusKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND name=N'uq_fn_workflow_task_member_assignee')
    CREATE UNIQUE INDEX uq_fn_workflow_task_member_assignee ON dbo.fn_workflow_task_member(TaskId, AssigneeUserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND name=N'uq_fn_workflow_task_member_sequence')
    CREATE UNIQUE INDEX uq_fn_workflow_task_member_sequence ON dbo.fn_workflow_task_member(TaskId, SequenceNo);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND name=N'uq_fn_workflow_task_member_todo')
    CREATE UNIQUE INDEX uq_fn_workflow_task_member_todo ON dbo.fn_workflow_task_member(TodoId) WHERE TodoId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND name=N'ix_fn_workflow_task_member_status')
    CREATE INDEX ix_fn_workflow_task_member_status ON dbo.fn_workflow_task_member(TaskId, StatusKey, SequenceNo);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_task') AND name=N'ck_fn_workflow_task_state')
    ALTER TABLE dbo.fn_workflow_task WITH CHECK ADD CONSTRAINT ck_fn_workflow_task_state CHECK (MemberCount BETWEEN 1 AND 20 AND RequiredCompletionCount BETWEEN 1 AND MemberCount AND Revision>0 AND CompletionModeKey IN ('all','any','sequential') AND ((CompletionModeKey='any' AND RequiredCompletionCount=1) OR (CompletionModeKey IN ('all','sequential') AND RequiredCompletionCount=MemberCount)) AND ((StatusKey='active' AND CompletedAtUtc IS NULL) OR (StatusKey IN ('completed','cancelled') AND CompletedAtUtc IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_task_member') AND name=N'ck_fn_workflow_task_member_state')
    ALTER TABLE dbo.fn_workflow_task_member WITH CHECK ADD CONSTRAINT ck_fn_workflow_task_member_state CHECK (SequenceNo BETWEEN 1 AND 20 AND Revision>0 AND ((StatusKey='pending' AND CompletedAtUtc IS NULL) OR (StatusKey='completed' AND TodoId IS NOT NULL AND CompletedAtUtc IS NOT NULL) OR (StatusKey='cancelled' AND CompletedAtUtc IS NOT NULL)));
GO

IF COL_LENGTH(N'dbo.fn_workflow_todo_transfer',N'TaskMemberId') IS NULL
    ALTER TABLE dbo.fn_workflow_todo_transfer ADD TaskMemberId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'TaskMemberId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'独立办理成员交接身份；审批转办为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'TaskMemberId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ix_fn_workflow_todo_transfer_task_member')
    CREATE INDEX ix_fn_workflow_todo_transfer_task_member ON dbo.fn_workflow_todo_transfer(TaskMemberId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'fk_fn_workflow_todo_transfer_task_member')
    ALTER TABLE dbo.fn_workflow_todo_transfer WITH CHECK ADD CONSTRAINT fk_fn_workflow_todo_transfer_task_member FOREIGN KEY(TaskMemberId) REFERENCES dbo.fn_workflow_task_member(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_member_kind')
    ALTER TABLE dbo.fn_workflow_todo_transfer WITH CHECK ADD CONSTRAINT ck_fn_workflow_todo_transfer_member_kind CHECK (SlotId IS NULL OR TaskMemberId IS NULL);
GO

-- 先建立包含显式管理员授权的新约束，再移除旧个人转办约束，恢复窗口不出现无约束写入。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_handoff')
    ALTER TABLE dbo.fn_workflow_todo_transfer WITH CHECK ADD CONSTRAINT ck_fn_workflow_todo_transfer_handoff CHECK (Depth BETWEEN 1 AND 32 AND FormRevision>0 AND FromAssigneeUserId<>ToAssigneeUserId AND (AuthorizationKey='workflow.instances.recover' OR (AuthorizationKey='workflow.todos.transfer' AND ActorUserId=FromAssigneeUserId)));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_identity')
    ALTER TABLE dbo.fn_workflow_todo_transfer DROP CONSTRAINT ck_fn_workflow_todo_transfer_identity;
GO
