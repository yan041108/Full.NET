IF OBJECT_ID(N'dbo.fn_seed_run', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_seed_run
    (
        Id uniqueidentifier NOT NULL,
        Profile varchar(16) NOT NULL,
        EnvironmentName nvarchar(64) NOT NULL,
        Status varchar(16) NOT NULL,
        ApplicationVersion varchar(64) NOT NULL,
        CorrelationId varchar(64) NOT NULL,
        StartedAt datetimeoffset(7) NOT NULL,
        CompletedAt datetimeoffset(7) NULL,
        ErrorCode varchar(128) NULL,
        CONSTRAINT PK_fn_seed_run PRIMARY KEY (Id),
        CONSTRAINT CK_fn_seed_run_Status
            CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Cancelled'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'种子数据执行运行记录', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'ApplicationVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'ApplicationVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'CompletedAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'CompletedAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'CorrelationId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'关联标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'CorrelationId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'EnvironmentName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'环境名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'EnvironmentName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'Profile', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'种子配置档', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'Profile';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'StartedAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'StartedAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'Status';
    CREATE INDEX IX_fn_seed_run_StartedAt ON dbo.fn_seed_run(StartedAt);
END;

IF OBJECT_ID(N'dbo.fn_seed_run_item', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_seed_run_item
    (
        RunId uniqueidentifier NOT NULL,
        Contributor varchar(128) NOT NULL,
        ContributorVersion int NOT NULL,
        Status varchar(16) NOT NULL,
        CreatedCount int NOT NULL,
        UpdatedCount int NOT NULL,
        SkippedCount int NOT NULL,
        StartedAt datetimeoffset(7) NOT NULL,
        CompletedAt datetimeoffset(7) NULL,
        ErrorCode varchar(128) NULL,
        CONSTRAINT PK_fn_seed_run_item PRIMARY KEY (RunId, Contributor),
        CONSTRAINT FK_fn_seed_run_item_Run
            FOREIGN KEY (RunId) REFERENCES dbo.fn_seed_run(Id),
        CONSTRAINT CK_fn_seed_run_item_Status
            CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Cancelled')),
        CONSTRAINT CK_fn_seed_run_item_Counts
            CHECK (CreatedCount >= 0 AND UpdatedCount >= 0 AND SkippedCount >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'种子数据贡献者执行明细', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'CompletedAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'CompletedAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'Contributor', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'贡献者名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'Contributor';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'ContributorVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'贡献者版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'ContributorVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'CreatedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'新建数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'CreatedCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'RunId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'RunId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'SkippedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'跳过数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'SkippedCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'StartedAt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'StartedAt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_seed_run_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_seed_run_item'), N'UpdatedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'UpdatedCount';
END;
