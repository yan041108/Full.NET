-- 扩展导入任务状态约束以允许本人待执行取消，不修改业务表、任务进度或私有输入。
-- 固定对象名与状态白名单；部分执行中断后可重入。撤回约束前须处理 canceled 数据，不能直接收窄为旧白名单。
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.fn_import_export_task')
        AND name=N'CK_fn_import_export_task_StatusKey' AND definition LIKE N'%canceled%'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_import_export_task') AND name=N'CK_fn_import_export_task_StatusKey')
        ALTER TABLE dbo.fn_import_export_task DROP CONSTRAINT CK_fn_import_export_task_StatusKey;
    ALTER TABLE dbo.fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_StatusKey
        CHECK (StatusKey IN (N'uploaded',N'preview_succeeded',N'preview_failed',N'queued',N'executing',N'execution_succeeded',N'execution_partial',N'execution_failed',N'canceled'));
END;
