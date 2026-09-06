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
