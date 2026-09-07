-- 154：平台授权备份执行器任务目录与运行结果表。

IF OBJECT_ID(N'dbo.fn_platform_backup_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_platform_backup_task
    (
        Id uniqueidentifier NOT NULL,
        TaskKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        DatabaseProvider varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_fn_platform_backup_task_IsEnabled DEFAULT (1),
        SortOrder int NOT NULL CONSTRAINT DF_fn_platform_backup_task_SortOrder DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_platform_backup_task PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_platform_backup_task_TaskKey UNIQUE (TaskKey),
        CONSTRAINT CK_fn_platform_backup_task_DatabaseProvider
            CHECK (DatabaseProvider IN (N'sql_server', N'mysql', N'all'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'平台备份任务表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'DatabaseProvider', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据库提供程序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'DatabaseProvider';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'DisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'DisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'SortOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'SortOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'TaskKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任务键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'TaskKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_task'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'平台授权备份任务目录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_task';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_platform_backup_task')
      AND indexObject.name = N'IX_fn_platform_backup_task_SortOrder'
)
    CREATE INDEX IX_fn_platform_backup_task_SortOrder
        ON dbo.fn_platform_backup_task(SortOrder, TaskKey);

IF OBJECT_ID(N'dbo.fn_platform_backup_run', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_platform_backup_run
    (
        Id uniqueidentifier NOT NULL,
        TaskId uniqueidentifier NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        ArtifactFileName nvarchar(260) NULL,
        ArtifactSizeBytes bigint NULL,
        ArtifactContentType varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        SummaryMessage nvarchar(2000) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_platform_backup_run PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_platform_backup_run_Task
            FOREIGN KEY (TaskId) REFERENCES dbo.fn_platform_backup_task (Id),
        CONSTRAINT CK_fn_platform_backup_run_Status
            CHECK (Status IN (N'pending', N'running', N'succeeded', N'failed'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'平台备份运行表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'ArtifactContentType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'产物内容类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'ArtifactContentType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'ArtifactFileName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'产物文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'ArtifactFileName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'ArtifactSizeBytes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'产物大小(字节)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'ArtifactSizeBytes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'StartedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'SummaryMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'摘要消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'SummaryMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_backup_run'), N'TaskId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任务标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run', @level2type=N'COLUMN', @level2name=N'TaskId';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'平台授权备份运行结果表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_backup_run';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_platform_backup_run')
      AND indexObject.name = N'IX_fn_platform_backup_run_TaskId_StartedAtUtc'
)
    CREATE INDEX IX_fn_platform_backup_run_TaskId_StartedAtUtc
        ON dbo.fn_platform_backup_run(TaskId, StartedAtUtc DESC, Id);

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.fn_platform_backup_task
    WHERE TaskKey = N'sql-server-full'
)
    INSERT INTO dbo.fn_platform_backup_task
        (Id, TaskKey, DisplayName, Description, DatabaseProvider, IsEnabled, SortOrder, CreatedAtUtc)
    VALUES
        (
            '01954f00-0001-7000-8000-000000000001',
            N'sql-server-full',
            N'SQL Server 全库备份',
            N'由授权备份执行器在受控凭据与对象存储前置条件下执行的 SQL Server 全库备份任务。',
            N'sql_server',
            1,
            10,
            SYSUTCDATETIME()
        );

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.fn_platform_backup_task
    WHERE TaskKey = N'mysql-full'
)
    INSERT INTO dbo.fn_platform_backup_task
        (Id, TaskKey, DisplayName, Description, DatabaseProvider, IsEnabled, SortOrder, CreatedAtUtc)
    VALUES
        (
            '01954f00-0001-7000-8000-000000000002',
            N'mysql-full',
            N'MySQL 全库备份',
            N'由授权备份执行器在受控凭据与对象存储前置条件下执行的 MySQL 全库备份任务。',
            N'mysql',
            1,
            20,
            SYSUTCDATETIME()
        );
