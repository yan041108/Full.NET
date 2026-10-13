-- 仅展开状态约束，保留全部业务行与检查点；旧 Worker 不会领取 cancelled。
-- 未记账时允许在旧约束、无约束及已展开约束上重入；回退前须停用取消入口并保留终态数据。
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_import_export_task') AND name = N'CK_fn_import_export_task_StatusKey')
    ALTER TABLE dbo.fn_import_export_task DROP CONSTRAINT CK_fn_import_export_task_StatusKey;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_import_export_task') AND name = N'CK_fn_import_export_task_StatusKey')
    ALTER TABLE dbo.fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_StatusKey
        CHECK (StatusKey IN (N'uploaded', N'preview_succeeded', N'preview_failed', N'queued', N'executing',
            N'execution_succeeded', N'execution_partial', N'execution_failed', N'cancelled'));
