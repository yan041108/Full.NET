-- 173：ImportExport 导入任务批量执行状态与检查点列。

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ProcessedRowCount') IS NULL
    ALTER TABLE dbo.fn_import_export_task
        ADD ProcessedRowCount int NOT NULL
            CONSTRAINT DF_fn_import_export_task_ProcessedRowCount DEFAULT 0;

IF COL_LENGTH(N'dbo.fn_import_export_task', N'SucceededRowCount') IS NULL
    ALTER TABLE dbo.fn_import_export_task
        ADD SucceededRowCount int NOT NULL
            CONSTRAINT DF_fn_import_export_task_SucceededRowCount DEFAULT 0;

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ExecutionFailedRowCount') IS NULL
    ALTER TABLE dbo.fn_import_export_task
        ADD ExecutionFailedRowCount int NOT NULL
            CONSTRAINT DF_fn_import_export_task_ExecutionFailedRowCount DEFAULT 0;

IF COL_LENGTH(N'dbo.fn_import_export_task', N'NextLineNumber') IS NULL
    ALTER TABLE dbo.fn_import_export_task
        ADD NextLineNumber int NOT NULL
            CONSTRAINT DF_fn_import_export_task_NextLineNumber DEFAULT 0;

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ExecutionRowsJson') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ExecutionRowsJson nvarchar(max) NULL;

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ErrorReceiptFileId') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ErrorReceiptFileId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ExecutionStartedAtUtc') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ExecutionStartedAtUtc datetimeoffset(7) NULL;

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ExecutionCompletedAtUtc') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ExecutionCompletedAtUtc datetimeoffset(7) NULL;

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_import_export_task')
      AND name = N'CK_fn_import_export_task_StatusKey'
)
    ALTER TABLE dbo.fn_import_export_task
        DROP CONSTRAINT CK_fn_import_export_task_StatusKey;

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_import_export_task')
      AND name = N'CK_fn_import_export_task_StatusKey'
)
    ALTER TABLE dbo.fn_import_export_task
        ADD CONSTRAINT CK_fn_import_export_task_StatusKey
            CHECK (StatusKey IN (
                N'uploaded',
                N'preview_succeeded',
                N'preview_failed',
                N'queued',
                N'executing',
                N'execution_succeeded',
                N'execution_partial',
                N'execution_failed'));
