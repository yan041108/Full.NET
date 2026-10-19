-- 269：为 PlatformDocument 日志增加关联快照；可空增量列，历史不回填，回退保留列。

IF COL_LENGTH(N'dbo.fn_document_access_log', N'CorrelationJson') IS NULL
    ALTER TABLE dbo.fn_document_access_log ADD CorrelationJson nvarchar(2048) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_document_access_log')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'),N'CorrelationJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件产生时的关联快照；历史缺失为空，不含凭据或原始IP',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log',
        @level2type=N'COLUMN', @level2name=N'CorrelationJson';

IF COL_LENGTH(N'dbo.fn_document_version_deletion_audit', N'CorrelationJson') IS NULL
    ALTER TABLE dbo.fn_document_version_deletion_audit ADD CorrelationJson nvarchar(2048) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'),N'CorrelationJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件产生时的关联快照；历史缺失为空，不含凭据或原始IP',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit',
        @level2type=N'COLUMN', @level2name=N'CorrelationJson';
