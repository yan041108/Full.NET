-- 只展开可空租约字段和收口索引，保留历史任务与结果引用；回退须先停用新 Worker。
IF COL_LENGTH(N'dbo.fn_document_preview_task', N'LeaseId') IS NULL
    ALTER TABLE dbo.fn_document_preview_task ADD LeaseId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.fn_document_preview_task', N'LeaseExpiresAtUtc') IS NULL
    ALTER TABLE dbo.fn_document_preview_task ADD LeaseExpiresAtUtc datetimeoffset(7) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'LeaseId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次处理租约身份',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'LeaseId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'LeaseExpiresAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'处理租约到期时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
    AND name = N'IX_fn_document_preview_task_StatusKey_LeaseExpiresAtUtc')
    EXEC(N'CREATE INDEX IX_fn_document_preview_task_StatusKey_LeaseExpiresAtUtc ON dbo.fn_document_preview_task (StatusKey, LeaseExpiresAtUtc, StartedAtUtc, CreatedAtUtc, Id)');
