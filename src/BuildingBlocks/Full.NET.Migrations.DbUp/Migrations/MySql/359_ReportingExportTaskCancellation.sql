-- 允许本人停止尚未领取的报表导出，仅扩展任务状态白名单，不回填或替换授权快照。
-- MySQL 按固定约束元数据选择字面 DDL，只有部署连接允许用户变量，业务连接不开放动态 SQL。
-- 部分完成可重放；收窄回旧约束前须处理 canceled 数据。
SET @fn_report_cancel_ready = EXISTS(SELECT 1 FROM information_schema.check_constraints
    WHERE constraint_schema=DATABASE() AND constraint_name='CK_fn_reporting_export_task_StatusKey' AND check_clause LIKE '%canceled%');
SET @fn_report_cancel_drop = IF(@fn_report_cancel_ready=0 AND EXISTS(SELECT 1 FROM information_schema.table_constraints
    WHERE constraint_schema=DATABASE() AND table_name='fn_reporting_export_task' AND constraint_name='CK_fn_reporting_export_task_StatusKey'),
    'ALTER TABLE fn_reporting_export_task DROP CHECK CK_fn_reporting_export_task_StatusKey','SELECT 1');
PREPARE fn_report_cancel_drop_stmt FROM @fn_report_cancel_drop;
EXECUTE fn_report_cancel_drop_stmt;
DEALLOCATE PREPARE fn_report_cancel_drop_stmt;
SET @fn_report_cancel_add = IF(@fn_report_cancel_ready=0,
    'ALTER TABLE fn_reporting_export_task ADD CONSTRAINT CK_fn_reporting_export_task_StatusKey CHECK (StatusKey IN (''queued'',''processing'',''succeeded'',''failed'',''canceled''))','SELECT 1');
PREPARE fn_report_cancel_add_stmt FROM @fn_report_cancel_add;
EXECUTE fn_report_cancel_add_stmt;
DEALLOCATE PREPARE fn_report_cancel_add_stmt;
