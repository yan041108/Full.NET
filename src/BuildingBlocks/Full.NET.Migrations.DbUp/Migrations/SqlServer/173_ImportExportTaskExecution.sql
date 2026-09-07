-- 173：ImportExport 导入任务批量执行状态与检查点列。

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ProcessedRowCount') IS NULL
    ALTER TABLE dbo.fn_import_export_task
        ADD ProcessedRowCount int NOT NULL
            CONSTRAINT DF_fn_import_export_task_ProcessedRowCount DEFAULT 0;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'ProcessedRowCount', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已处理行数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'ProcessedRowCount';

IF COL_LENGTH(N'dbo.fn_import_export_task', N'SucceededRowCount') IS NULL
    ALTER TABLE dbo.fn_import_export_task
        ADD SucceededRowCount int NOT NULL
            CONSTRAINT DF_fn_import_export_task_SucceededRowCount DEFAULT 0;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'SucceededRowCount', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'成功行数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'SucceededRowCount';

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ExecutionFailedRowCount') IS NULL
    ALTER TABLE dbo.fn_import_export_task
        ADD ExecutionFailedRowCount int NOT NULL
            CONSTRAINT DF_fn_import_export_task_ExecutionFailedRowCount DEFAULT 0;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'ExecutionFailedRowCount', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行失败行数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'ExecutionFailedRowCount';

IF COL_LENGTH(N'dbo.fn_import_export_task', N'NextLineNumber') IS NULL
    ALTER TABLE dbo.fn_import_export_task
        ADD NextLineNumber int NOT NULL
            CONSTRAINT DF_fn_import_export_task_NextLineNumber DEFAULT 0;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'NextLineNumber', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下一行号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'NextLineNumber';

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ExecutionRowsJson') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ExecutionRowsJson nvarchar(max) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'ExecutionRowsJson', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Execution Rows(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'ExecutionRowsJson';

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ErrorReceiptFileId') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ErrorReceiptFileId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'ErrorReceiptFileId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误回执文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'ErrorReceiptFileId';

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ExecutionStartedAtUtc') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ExecutionStartedAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'ExecutionStartedAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Execution Started At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'ExecutionStartedAtUtc';

IF COL_LENGTH(N'dbo.fn_import_export_task', N'ExecutionCompletedAtUtc') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD ExecutionCompletedAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'ExecutionCompletedAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Execution Completed At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'ExecutionCompletedAtUtc';

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
