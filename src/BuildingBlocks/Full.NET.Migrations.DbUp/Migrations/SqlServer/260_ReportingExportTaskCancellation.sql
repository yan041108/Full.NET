-- 只展开取消终态，既有任务和文件不回填、不删除；未记账的缺约束及已展开阶段可重入。
-- 混合版本仍可读取稳定字符串状态；回退前停用取消入口，保留已取消行，禁止收窄约束。
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_reporting_export_task') AND name = N'CK_fn_reporting_export_task_StatusKey')
    ALTER TABLE dbo.fn_reporting_export_task DROP CONSTRAINT CK_fn_reporting_export_task_StatusKey;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_reporting_export_task') AND name = N'CK_fn_reporting_export_task_StatusKey')
    ALTER TABLE dbo.fn_reporting_export_task ADD CONSTRAINT CK_fn_reporting_export_task_StatusKey
        CHECK (StatusKey IN (N'queued', N'processing', N'succeeded', N'failed', N'cancelled'));
