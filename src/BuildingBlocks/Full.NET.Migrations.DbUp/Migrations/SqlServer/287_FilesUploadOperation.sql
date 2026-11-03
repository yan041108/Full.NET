-- 上传操作身份与 pending 文件同一行持久化，防止响应丢失导致重复文件。
IF COL_LENGTH('dbo.fn_files_file', 'UploadOperationKey') IS NULL
    ALTER TABLE dbo.fn_files_file ADD UploadOperationKey varchar(128) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_files_file') AND name = 'UX_fn_files_file_UploadOperationKey')
    CREATE UNIQUE INDEX UX_fn_files_file_UploadOperationKey ON dbo.fn_files_file (UploadOperationKey) WHERE UploadOperationKey IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_files_file') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_files_file'), 'UploadOperationKey', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受控调用者上传操作幂等键，未知发布结果禁止盲目重传', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'UploadOperationKey';
