-- 288：启动回执独立于运行状态；原始响应可在终态后幂等重放。
IF OBJECT_ID(N'dbo.fn_workflow_start_receipt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_start_receipt (
        InstanceId uniqueidentifier NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ResponseJson nvarchar(max) NOT NULL,
        CONSTRAINT pk_fn_workflow_start_receipt PRIMARY KEY CLUSTERED (InstanceId),
        CONSTRAINT uq_fn_workflow_start_receipt_request UNIQUE (TenantScopeKey, ActorUserId, IdempotencyKey),
        CONSTRAINT fk_fn_workflow_start_receipt_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_start_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_start_receipt'), N'InstanceId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_start_receipt', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_start_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_start_receipt'), N'TenantScopeKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户作用域', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_start_receipt', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_start_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_start_receipt'), N'ActorUserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发起人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_start_receipt', @level2type=N'COLUMN', @level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_start_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_start_receipt'), N'IdempotencyKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'启动幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_start_receipt', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_start_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_start_receipt'), N'RequestHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'启动请求摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_start_receipt', @level2type=N'COLUMN', @level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_start_receipt') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_start_receipt'), N'ResponseJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可变启动响应', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_start_receipt', @level2type=N'COLUMN', @level2name=N'ResponseJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_start_receipt') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流不可变启动回执', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_start_receipt';
GO

-- 固定输入的续办工作项；取消提升代次，旧租约不能回写结果。
IF OBJECT_ID(N'dbo.fn_workflow_activity_work', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_activity_work (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        NodeKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        InputJson nvarchar(max) NOT NULL,
        InputHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        InputFormRevision bigint NOT NULL,
        ExecutionSequence bigint NOT NULL,
        ParallelJoinId uniqueidentifier NULL,
        ParallelBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        DueAtUtc datetimeoffset(7) NOT NULL,
        StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        LeaseGeneration bigint NOT NULL,
        AttemptCount int NOT NULL,
        LeaseOwnerKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        LeaseExpiresAtUtc datetimeoffset(7) NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        ResultStatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
        ResultTodoId uniqueidentifier NULL,
        CONSTRAINT pk_fn_workflow_activity_work PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_activity_work_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_activity_work') AND name=N'ix_fn_workflow_activity_work_due')
    CREATE INDEX ix_fn_workflow_activity_work_due ON dbo.fn_workflow_activity_work (StatusKey, DueAtUtc, Id) INCLUDE (InstanceId, LeaseExpiresAtUtc);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'耐久工作标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'InstanceId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'TenantScopeKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户作用域', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'NodeKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'续办节点键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'InputJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定执行输入', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'InputHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输入摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'InputHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'InputFormRevision', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输入表单修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'ExecutionSequence', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排队时执行序号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'ExecutionSequence';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'ParallelJoinId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属分叉激活标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'ParallelBranchKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属分支键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'ParallelBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'DueAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最早续办时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'DueAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'StatusKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'LeaseGeneration', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约代次', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'LeaseGeneration';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'AttemptCount', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'领取次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'AttemptCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'LeaseOwnerKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约持有者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'LeaseOwnerKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'LeaseExpiresAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约截止时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'CompletedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作结束时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'ResultStatusKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提交结果状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'ResultStatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_activity_work'), N'ResultTodoId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果首待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work', @level2type=N'COLUMN', @level2name=N'ResultTodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_activity_work') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流耐久续办活动', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_activity_work';
GO

IF OBJECT_ID(N'dbo.fn_workflow_branch_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_branch_state (
        ParallelJoinId uniqueidentifier NOT NULL,
        BranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        InputJson nvarchar(max) NOT NULL,
        InputFormRevision bigint NOT NULL,
        OutputJson nvarchar(max) NOT NULL,
        Revision bigint NOT NULL,
        CONSTRAINT pk_fn_workflow_branch_state PRIMARY KEY CLUSTERED (ParallelJoinId, BranchKey),
        CONSTRAINT fk_fn_workflow_branch_state_join FOREIGN KEY (ParallelJoinId) REFERENCES dbo.fn_workflow_parallel_join (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_state') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_state'), N'ParallelJoinId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属分叉激活标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_state', @level2type=N'COLUMN', @level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_state') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_state'), N'BranchKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'稳定分支键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_state', @level2type=N'COLUMN', @level2name=N'BranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_state') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_state'), N'InputJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'共同冻结输入', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_state', @level2type=N'COLUMN', @level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_state') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_state'), N'InputFormRevision', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'共同输入表单修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_state', @level2type=N'COLUMN', @level2name=N'InputFormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_state') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_state'), N'OutputJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分支局部输出补丁', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_state', @level2type=N'COLUMN', @level2name=N'OutputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_state') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_state'), N'Revision', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分支输出修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_state', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_state') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流分支输入与独立输出', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_state';
GO

IF OBJECT_ID(N'dbo.fn_workflow_branch_merge', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_branch_merge (
        ParallelJoinId uniqueidentifier NOT NULL,
        FormRevision bigint NOT NULL,
        OutputJson nvarchar(max) NOT NULL,
        MergedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT pk_fn_workflow_branch_merge PRIMARY KEY CLUSTERED (ParallelJoinId),
        CONSTRAINT fk_fn_workflow_branch_merge_join FOREIGN KEY (ParallelJoinId) REFERENCES dbo.fn_workflow_parallel_join (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_merge') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_merge'), N'ParallelJoinId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属分叉激活标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_merge', @level2type=N'COLUMN', @level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_merge') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_merge'), N'FormRevision', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'合并后的表单修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_merge', @level2type=N'COLUMN', @level2name=N'FormRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_merge') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_merge'), N'OutputJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定合并结果', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_merge', @level2type=N'COLUMN', @level2name=N'OutputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_merge') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_branch_merge'), N'MergedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'合并时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_merge', @level2type=N'COLUMN', @level2name=N'MergedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_branch_merge') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流分支原子合并回执', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_branch_merge';
GO

IF OBJECT_ID(N'dbo.fn_workflow_fork_context', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_fork_context (
        ParallelJoinId uniqueidentifier NOT NULL,
        ParentParallelJoinId uniqueidentifier NULL,
        ParentBranchKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        CONSTRAINT pk_fn_workflow_fork_context PRIMARY KEY CLUSTERED (ParallelJoinId),
        CONSTRAINT fk_fn_workflow_fork_context_join FOREIGN KEY (ParallelJoinId) REFERENCES dbo.fn_workflow_parallel_join (Id),
        CONSTRAINT fk_fn_workflow_fork_context_parent FOREIGN KEY (ParentParallelJoinId) REFERENCES dbo.fn_workflow_parallel_join (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_fork_context') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_fork_context'), N'ParallelJoinId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分叉激活标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_fork_context', @level2type=N'COLUMN', @level2name=N'ParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_fork_context') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_fork_context'), N'ParentParallelJoinId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'父分叉激活标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_fork_context', @level2type=N'COLUMN', @level2name=N'ParentParallelJoinId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_fork_context') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_fork_context'), N'ParentBranchKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'父分支稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_fork_context', @level2type=N'COLUMN', @level2name=N'ParentBranchKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_workflow_fork_context') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流分叉激活所属执行范围', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_fork_context';
GO
