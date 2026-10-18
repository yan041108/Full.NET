-- MySQL DDL 隐式提交；分别探测删除与展开阶段，任一阶段中断且未记账时均可恢复。
-- 仅增加取消终态，不回填或删除任务与结果文件；回退停用入口并保留终态数据。
SET @constraint_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task'
      AND CONSTRAINT_NAME = 'CK_fn_reporting_export_task_StatusKey');
SET @ddl := IF(@constraint_exists > 0,
    'ALTER TABLE fn_reporting_export_task DROP CHECK CK_fn_reporting_export_task_StatusKey', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_export_task'
      AND CONSTRAINT_NAME = 'CK_fn_reporting_export_task_StatusKey');
SET @ddl := IF(@constraint_exists = 0,
    'ALTER TABLE fn_reporting_export_task ADD CONSTRAINT CK_fn_reporting_export_task_StatusKey CHECK (StatusKey IN (''queued'', ''processing'', ''succeeded'', ''failed'', ''cancelled''))', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
