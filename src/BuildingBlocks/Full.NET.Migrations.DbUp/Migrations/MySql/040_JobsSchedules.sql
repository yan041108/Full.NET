-- 040：MySQL DDL 会隐式提交，逐项收敛计划表、执行关联列和索引。
CREATE TABLE IF NOT EXISTS fn_jobs_schedule
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    JobDefinitionId BINARY(16) NOT NULL,
    TriggerKind varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CronExpression varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL,
    TimeZoneId varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    OneTimeAtUtc datetime(6) NULL,
    MisfirePolicy varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    IsEnabled boolean NOT NULL,
    NextExecutionAtUtc datetime(6) NULL,
    LastExecutionAtUtc datetime(6) NULL,
    CompletedAtUtc datetime(6) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CreatedByUserId BINARY(16) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    UpdatedByUserId BINARY(16) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_jobs_schedule PRIMARY KEY (Id),
    CONSTRAINT FK_fn_jobs_schedule_Definition
        FOREIGN KEY (JobDefinitionId)
        REFERENCES fn_jobs_definition(Id),
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
        ),
    KEY IX_fn_jobs_schedule_JobDefinitionId (JobDefinitionId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_jobs_schedule_migrate;
DELIMITER $$
CREATE PROCEDURE fn_jobs_schedule_migrate()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_execution'
          AND COLUMN_NAME = 'JobScheduleId'
    ) THEN
        ALTER TABLE fn_jobs_execution
            ADD COLUMN JobScheduleId BINARY(16) NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_execution'
          AND COLUMN_NAME = 'ScheduledForUtc'
    ) THEN
        ALTER TABLE fn_jobs_execution
            ADD COLUMN ScheduledForUtc datetime(6) NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_schedule'
          AND INDEX_NAME = 'IX_fn_jobs_schedule_Due'
    ) THEN
        CREATE INDEX IX_fn_jobs_schedule_Due
            ON fn_jobs_schedule
                (TenantId, IsEnabled, NextExecutionAtUtc, Id);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_execution'
          AND INDEX_NAME =
              'IX_fn_jobs_execution_JobScheduleCreatedAtUtc'
    ) THEN
        CREATE INDEX IX_fn_jobs_execution_JobScheduleCreatedAtUtc
            ON fn_jobs_execution(JobScheduleId, CreatedAtUtc, Id);
    END IF;
END$$
DELIMITER ;

CALL fn_jobs_schedule_migrate();
DROP PROCEDURE fn_jobs_schedule_migrate;
