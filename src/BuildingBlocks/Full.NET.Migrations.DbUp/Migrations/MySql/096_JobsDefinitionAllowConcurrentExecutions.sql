-- 096：MySQL 为作业定义增加是否允许重叠执行，与 SqlServer 096 同构。

DROP PROCEDURE IF EXISTS fn_jobs_definition_allow_concurrent_executions;
DELIMITER $$
CREATE PROCEDURE fn_jobs_definition_allow_concurrent_executions()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_definition'
          AND COLUMN_NAME = 'AllowConcurrentExecutions'
    ) THEN
        ALTER TABLE fn_jobs_definition
            ADD AllowConcurrentExecutions tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允许同一作业重叠执行';
    END IF;
END$$
DELIMITER ;
CALL fn_jobs_definition_allow_concurrent_executions();
DROP PROCEDURE fn_jobs_definition_allow_concurrent_executions;
