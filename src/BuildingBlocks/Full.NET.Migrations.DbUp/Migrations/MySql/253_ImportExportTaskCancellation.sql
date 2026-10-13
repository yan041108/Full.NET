-- MySQL DDL 隐式提交，分别探测删除与展开阶段；任一阶段中断且未记账时均可恢复。
-- 只增加 cancelled 状态，不回填或删除业务行；回退前须停用取消入口并保留终态数据。
SET @constraint_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task'
      AND CONSTRAINT_NAME = 'CK_fn_import_export_task_StatusKey');
SET @ddl := IF(@constraint_exists > 0,
    'ALTER TABLE fn_import_export_task DROP CHECK CK_fn_import_export_task_StatusKey', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_import_export_task'
      AND CONSTRAINT_NAME = 'CK_fn_import_export_task_StatusKey');
SET @ddl := IF(@constraint_exists = 0,
    'ALTER TABLE fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_StatusKey CHECK (StatusKey IN (''uploaded'', ''preview_succeeded'', ''preview_failed'', ''queued'', ''executing'', ''execution_succeeded'', ''execution_partial'', ''execution_failed'', ''cancelled''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
