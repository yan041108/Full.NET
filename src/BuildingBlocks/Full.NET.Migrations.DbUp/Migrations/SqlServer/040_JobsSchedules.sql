-- 040：Host 一次性/Cron 计划，以及执行记录到计划和预定时刻的可追溯关联。
IF OBJECT_ID(N'dbo.fn_jobs_schedule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_jobs_schedule
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        JobDefinitionId uniqueidentifier NOT NULL,
        TriggerKind varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CronExpression varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        TimeZoneId varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OneTimeAtUtc datetimeoffset(7) NULL,
        MisfirePolicy varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IsEnabled bit NOT NULL,
        NextExecutionAtUtc datetimeoffset(7) NULL,
        LastExecutionAtUtc datetimeoffset(7) NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedByUserId uniqueidentifier NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_jobs_schedule_Version DEFAULT (1),
        CONSTRAINT PK_fn_jobs_schedule PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_jobs_schedule_Definition
            FOREIGN KEY (JobDefinitionId)
            REFERENCES dbo.fn_jobs_definition(Id),
        CONSTRAINT CK_fn_jobs_schedule_TriggerKind
            CHECK (TriggerKind IN ('one_time', 'cron')),
        CONSTRAINT CK_fn_jobs_schedule_MisfirePolicy
            CHECK (MisfirePolicy IN ('skip', 'fire_once')),
        CONSTRAINT CK_fn_jobs_schedule_TriggerShape
            CHECK
            (
                (TriggerKind = 'one_time'
                 AND CronExpression IS NULL
                 AND OneTimeAtUtc IS NOT NULL
                 AND MisfirePolicy = 'fire_once')
                OR
                (TriggerKind = 'cron'
                 AND CronExpression IS NOT NULL
                 AND OneTimeAtUtc IS NULL)
            )
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'后台任务调度表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'CronExpression', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Cron 表达式', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'CronExpression';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'JobDefinitionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任务定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'JobDefinitionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'LastExecutionAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后执行时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'LastExecutionAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'MisfirePolicy', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错过触发策略', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'MisfirePolicy';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'NextExecutionAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下次执行时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'NextExecutionAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'OneTimeAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'一次性触发时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'OneTimeAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'TimeZoneId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'时区标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'TimeZoneId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'TriggerKind', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'触发类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'TriggerKind';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'UpdatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF COL_LENGTH(N'dbo.fn_jobs_execution', N'JobScheduleId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_execution
        ADD JobScheduleId uniqueidentifier NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_jobs_execution')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_execution'), N'JobScheduleId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任务调度标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'JobScheduleId';
END;

IF COL_LENGTH(N'dbo.fn_jobs_execution', N'ScheduledForUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_execution
        ADD ScheduledForUtc datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_jobs_execution')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_execution'), N'ScheduledForUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'计划执行时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'ScheduledForUtc';
END;

-- 新增列与引用该列的索引必须分批编译，否则恢复路径会在 ALTER 执行前解析失败。
GO

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
      AND name = N'IX_fn_jobs_schedule_Due'
)
AND
(
    SELECT STRING_AGG(
               CONVERT(nvarchar(max), columnObject.name),
               N',') WITHIN GROUP (ORDER BY indexColumn.key_ordinal)
    FROM sys.indexes AS indexObject
    INNER JOIN sys.index_columns AS indexColumn
        ON indexColumn.object_id = indexObject.object_id
       AND indexColumn.index_id = indexObject.index_id
       AND indexColumn.key_ordinal > 0
    INNER JOIN sys.columns AS columnObject
        ON columnObject.object_id = indexColumn.object_id
       AND columnObject.column_id = indexColumn.column_id
    WHERE indexObject.object_id =
          OBJECT_ID(N'dbo.fn_jobs_schedule')
      AND indexObject.name = N'IX_fn_jobs_schedule_Due'
) <> N'NextExecutionAtUtc,Id'
BEGIN
    DROP INDEX IX_fn_jobs_schedule_Due
        ON dbo.fn_jobs_schedule;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
      AND name = N'IX_fn_jobs_schedule_Due'
)
BEGIN
    CREATE INDEX IX_fn_jobs_schedule_Due
        ON dbo.fn_jobs_schedule(NextExecutionAtUtc, Id)
        INCLUDE (JobDefinitionId, TriggerKind, CronExpression, TimeZoneId,
                 OneTimeAtUtc, MisfirePolicy, Version)
        WHERE TenantId IS NULL
          AND IsEnabled = 1
          AND CompletedAtUtc IS NULL
          AND NextExecutionAtUtc IS NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_jobs_execution')
      AND name = N'IX_fn_jobs_execution_JobScheduleCreatedAtUtc'
)
BEGIN
    CREATE INDEX IX_fn_jobs_execution_JobScheduleCreatedAtUtc
        ON dbo.fn_jobs_execution(JobScheduleId, CreatedAtUtc DESC, Id)
        WHERE JobScheduleId IS NOT NULL;
END;
