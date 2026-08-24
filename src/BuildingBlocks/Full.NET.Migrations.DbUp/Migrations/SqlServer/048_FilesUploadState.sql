-- 048：以 pending/publishing/ready 状态隔离上传提交不确定性，存量文件保持可见。
IF COL_LENGTH(N'dbo.fn_files_file', N'StorageState') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file
        ADD StorageState varchar(16) COLLATE Latin1_General_100_BIN2 NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_files_file')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file'), N'StorageState', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file', @level2type=N'COLUMN', @level2name=N'StorageState';
END;

EXEC(N'
    UPDATE dbo.fn_files_file
    SET StorageState = ''ready''
    WHERE StorageState IS NULL OR StorageState = '''';

    ALTER TABLE dbo.fn_files_file
        ALTER COLUMN StorageState varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL;
');

IF OBJECT_ID(N'dbo.CK_fn_files_file_StorageState', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.fn_files_file
        DROP CONSTRAINT CK_fn_files_file_StorageState;
END;

EXEC(N'
    ALTER TABLE dbo.fn_files_file WITH CHECK
        ADD CONSTRAINT CK_fn_files_file_StorageState
            CHECK (StorageState IN (''pending'', ''publishing'', ''ready''));

    ALTER TABLE dbo.fn_files_file
        CHECK CONSTRAINT CK_fn_files_file_StorageState;
');
