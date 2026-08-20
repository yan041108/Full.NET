-- 098：MySQL 为任务定义增加 HandlerKind 与 ArgsJson，与 SqlServer 098 同构。

DROP PROCEDURE IF EXISTS fn_jobs_definition_handler_kind_and_args;
DELIMITER $$
CREATE PROCEDURE fn_jobs_definition_handler_kind_and_args()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_definition'
          AND COLUMN_NAME = 'HandlerKind'
    ) THEN
        ALTER TABLE fn_jobs_definition
            ADD HandlerKind varchar(32) NOT NULL DEFAULT 'ping' COMMENT '内置执行器稳定机器码';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_definition'
          AND COLUMN_NAME = 'ArgsJson'
    ) THEN
        ALTER TABLE fn_jobs_definition
            ADD ArgsJson longtext NULL COMMENT '执行参数 JSON；ping 必须为 NULL';
    END IF;
END$$
DELIMITER ;
CALL fn_jobs_definition_handler_kind_and_args();
DROP PROCEDURE fn_jobs_definition_handler_kind_and_args;
