-- 089：MySQL DDL 会隐式提交，逐项收敛作业定义与任务计划的新增列。
-- 与 SqlServer 089 保持同构：作业分组、运行统计、时间窗口与触发器参数。

DROP PROCEDURE IF EXISTS fn_jobs_definition_schedule_enhance;
DELIMITER $$
CREATE PROCEDURE fn_jobs_definition_schedule_enhance()
BEGIN
    -- 作业定义：作业分组，对应 Admin.NET SysJobDetail.GroupName。
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_definition'
          AND COLUMN_NAME = 'GroupName'
    ) THEN
        ALTER TABLE fn_jobs_definition ADD GroupName varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '分组名称';
    END IF;

    -- 任务计划：触发次数，计划每次实际创建执行记录时由调度器递增。
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_schedule'
          AND COLUMN_NAME = 'NumberOfRuns'
    ) THEN
        ALTER TABLE fn_jobs_schedule ADD NumberOfRuns bigint NOT NULL DEFAULT 0 COMMENT '运行次数';
    END IF;

    -- 任务计划：出错次数，执行记录终态为 failed 时由执行器递增。
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_schedule'
          AND COLUMN_NAME = 'NumberOfErrors'
    ) THEN
        ALTER TABLE fn_jobs_schedule ADD NumberOfErrors bigint NOT NULL DEFAULT 0 COMMENT '错误次数';
    END IF;

    -- 任务计划：生效起始时刻（UTC）。
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_schedule'
          AND COLUMN_NAME = 'StartTime'
    ) THEN
        ALTER TABLE fn_jobs_schedule ADD StartTime datetime(6) NULL COMMENT 'Start Time';
    END IF;

    -- 任务计划：失效结束时刻（UTC）。
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_schedule'
          AND COLUMN_NAME = 'EndTime'
    ) THEN
        ALTER TABLE fn_jobs_schedule ADD EndTime datetime(6) NULL COMMENT '结束时间';
    END IF;

    -- 任务计划：触发器参数，存储传给作业处理器的参数文本。
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_schedule'
          AND COLUMN_NAME = 'Args'
    ) THEN
        ALTER TABLE fn_jobs_schedule ADD Args varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '调度参数(JSON)';
    END IF;
END$$
DELIMITER ;

CALL fn_jobs_definition_schedule_enhance();
DROP PROCEDURE fn_jobs_definition_schedule_enhance;
