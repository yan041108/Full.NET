-- 允许本人停止尚未领取的报表导出，只扩展任务状态约束，不修改参数、权限或原会话。
-- 固定状态白名单，部分 DDL 失败可重入；收窄回旧约束前须处理 canceled 数据。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.fn_reporting_export_task')
        AND name=N'CK_fn_reporting_export_task_StatusKey' AND definition LIKE N'%canceled%')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_reporting_export_task') AND name=N'CK_fn_reporting_export_task_StatusKey')
        ALTER TABLE dbo.fn_reporting_export_task DROP CONSTRAINT CK_fn_reporting_export_task_StatusKey;
    ALTER TABLE dbo.fn_reporting_export_task ADD CONSTRAINT CK_fn_reporting_export_task_StatusKey
        CHECK (StatusKey IN (N'queued',N'processing',N'succeeded',N'failed',N'canceled'));
END;
