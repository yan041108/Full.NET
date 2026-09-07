-- 115：为并行网关分叉与汇合引入汇合状态表、分支到达事实表，并为步骤补充并行上下文列。
IF OBJECT_ID(N'dbo.fn_workflow_parallel_join', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_parallel_join
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        ForkNodeKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        JoinNodeKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequiredBranchCount int NOT NULL,
        ArrivedBranchCount int NOT NULL,
        StatusKey varchar(16) NOT NULL,
        Revision bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_workflow_parallel_join PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_parallel_join_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT CK_fn_workflow_parallel_join_RequiredBranchCount CHECK (RequiredBranchCount >= 2 AND RequiredBranchCount <= 8),
        CONSTRAINT CK_fn_workflow_parallel_join_ArrivedBranchCount CHECK (ArrivedBranchCount >= 0 AND ArrivedBranchCount <= RequiredBranchCount),
        CONSTRAINT CK_fn_workflow_parallel_join_Revision CHECK (Revision > 0),
        CONSTRAINT CK_fn_workflow_parallel_join_Status CHECK (StatusKey IN ('waiting', 'completed', 'cancelled'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流并行汇合表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'ArrivedBranchCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已到达分支数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'ArrivedBranchCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'ForkNodeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分叉节点键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'ForkNodeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'JoinNodeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'汇合节点键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'JoinNodeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'RequiredBranchCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所需分支数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'RequiredBranchCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'Revision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'Revision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_join'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'StatusKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流并行汇合状态表',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'InstanceId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分叉节点键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'ForkNodeKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'汇合节点键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'JoinNodeKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'需要到达汇合的分支总数',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'RequiredBranchCount';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已到达汇合的分支数',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'ArrivedBranchCount';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'汇合状态键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'StatusKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发修订号',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'Revision';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'汇合完成时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
END;

IF OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_parallel_branch_arrival
    (
        Id uniqueidentifier NOT NULL,
        ParallelJoinId uniqueidentifier NOT NULL,
        BranchKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ArrivedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_workflow_parallel_branch_arrival PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_parallel_branch_arrival_Join FOREIGN KEY (ParallelJoinId) REFERENCES dbo.fn_workflow_parallel_join(Id),
        CONSTRAINT UQ_fn_workflow_parallel_branch_arrival_Join_Branch UNIQUE (ParallelJoinId, BranchKey)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流并行分支到达表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival'), N'ArrivedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Arrived At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival', @level2type=N'COLUMN', @level2name=N'ArrivedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival'), N'BranchKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分支键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival', @level2type=N'COLUMN', @level2name=N'BranchKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_parallel_branch_arrival'), N'ParallelJoinId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'并行汇合标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival', @level2type=N'COLUMN', @level2name=N'ParallelJoinId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流并行分支到达事实表',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属汇合状态标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival', @level2type=N'COLUMN', @level2name=N'ParallelJoinId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'稳定分支键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival', @level2type=N'COLUMN', @level2name=N'BranchKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'到达汇合时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_branch_arrival', @level2type=N'COLUMN', @level2name=N'ArrivedAtUtc';
END;

IF COL_LENGTH(N'dbo.fn_workflow_step', N'ParallelJoinId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_workflow_step ADD ParallelJoinId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'ParallelJoinId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'并行汇合标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'ParallelJoinId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'并行汇合状态标识；非并行步骤为空',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'ParallelJoinId';
END;

IF COL_LENGTH(N'dbo.fn_workflow_step', N'ParallelBranchKey') IS NULL
BEGIN
    ALTER TABLE dbo.fn_workflow_step ADD ParallelBranchKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'ParallelBranchKey', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'并行分支键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'ParallelBranchKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'并行分支键；非并行步骤为空',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'ParallelBranchKey';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join')
      AND name = N'IX_fn_workflow_parallel_join_Instance_Status')
    CREATE INDEX IX_fn_workflow_parallel_join_Instance_Status
        ON dbo.fn_workflow_parallel_join(InstanceId, StatusKey);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_step')
      AND name = N'IX_fn_workflow_step_ParallelJoin')
    CREATE INDEX IX_fn_workflow_step_ParallelJoin
        ON dbo.fn_workflow_step(ParallelJoinId)
        WHERE ParallelJoinId IS NOT NULL;
