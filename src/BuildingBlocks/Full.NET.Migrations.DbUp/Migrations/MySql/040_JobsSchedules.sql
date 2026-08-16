-- 040：MySQL DDL 会隐式提交，逐项收敛计划表、执行关联列和索引。
CREATE TABLE IF NOT EXISTS fn_jobs_schedule (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    JobDefinitionId BINARY(16) NOT NULL COMMENT '任务定义标识',
    TriggerKind varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '触发类型',
    CronExpression varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT 'Cron 表达式',
    TimeZoneId varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '时区标识',
    OneTimeAtUtc datetime(6) NULL COMMENT '一次性触发时间(UTC)',
    MisfirePolicy varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '错过触发策略',
    IsEnabled boolean NOT NULL COMMENT '是否启用',
    NextExecutionAtUtc datetime(6) NULL COMMENT '下次执行时间(UTC)',
    LastExecutionAtUtc datetime(6) NULL COMMENT '最后执行时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    UpdatedByUserId BINARY(16) NULL COMMENT '更新人用户标识',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
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
) COMMENT='后台任务调度表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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
        ALTER TABLE fn_jobs_execution ADD JobScheduleId BINARY(16) NULL COMMENT '任务调度标识';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_execution'
          AND COLUMN_NAME = 'ScheduledForUtc'
    ) THEN
        ALTER TABLE fn_jobs_execution ADD ScheduledForUtc datetime(6) NULL COMMENT '计划执行时间(UTC)';
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
