-- 扩展导入任务状态约束以允许本人待执行取消，业务表、进度和私有输入保持。
-- MySQL 以固定 DDL 的 PREPARE 按约束元数据判断重入；仅部署连接允许用户变量，不接受运行时标识符。
-- 部分 DDL 中断后可重放；撤回约束前须处理 canceled 数据，不能直接收窄为旧白名单。
SET @fn_import_cancel_ready = EXISTS(SELECT 1 FROM information_schema.check_constraints
    WHERE constraint_schema=DATABASE() AND constraint_name='CK_fn_import_export_task_StatusKey' AND check_clause LIKE '%canceled%');
SET @fn_import_cancel_drop = IF(@fn_import_cancel_ready=0 AND EXISTS(SELECT 1 FROM information_schema.table_constraints
    WHERE constraint_schema=DATABASE() AND table_name='fn_import_export_task' AND constraint_name='CK_fn_import_export_task_StatusKey'),
    'ALTER TABLE fn_import_export_task DROP CHECK CK_fn_import_export_task_StatusKey','SELECT 1');
PREPARE fn_import_cancel_drop_stmt FROM @fn_import_cancel_drop;
EXECUTE fn_import_cancel_drop_stmt;
DEALLOCATE PREPARE fn_import_cancel_drop_stmt;
SET @fn_import_cancel_add = IF(@fn_import_cancel_ready=0,
    'ALTER TABLE fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_StatusKey CHECK (StatusKey IN (''uploaded'',''preview_succeeded'',''preview_failed'',''queued'',''executing'',''execution_succeeded'',''execution_partial'',''execution_failed'',''canceled''))','SELECT 1');
PREPARE fn_import_cancel_add_stmt FROM @fn_import_cancel_add;
EXECUTE fn_import_cancel_add_stmt;
DEALLOCATE PREPARE fn_import_cancel_add_stmt;
