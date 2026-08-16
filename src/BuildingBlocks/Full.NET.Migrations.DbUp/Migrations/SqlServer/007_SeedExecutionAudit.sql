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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'种子数据执行运行记录', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'ApplicationVersion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'CompletedAt';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'关联标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'CorrelationId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'环境名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'EnvironmentName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'种子配置档', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'Profile';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run', @level2type=N'COLUMN', @level2name=N'StartedAt';
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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'种子数据贡献者执行明细', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'CompletedAt';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'贡献者名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'Contributor';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'贡献者版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'ContributorVersion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'新建数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'CreatedCount';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'RunId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'跳过数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'SkippedCount';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'StartedAt';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'Status';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_seed_run_item', @level2type=N'COLUMN', @level2name=N'UpdatedCount';
END;
