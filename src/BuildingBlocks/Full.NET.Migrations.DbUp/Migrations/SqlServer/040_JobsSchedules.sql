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
END;

IF COL_LENGTH(N'dbo.fn_jobs_execution', N'JobScheduleId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_execution
        ADD JobScheduleId uniqueidentifier NULL;
END;

IF COL_LENGTH(N'dbo.fn_jobs_execution', N'ScheduledForUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_execution
        ADD ScheduledForUtc datetimeoffset(7) NULL;
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
