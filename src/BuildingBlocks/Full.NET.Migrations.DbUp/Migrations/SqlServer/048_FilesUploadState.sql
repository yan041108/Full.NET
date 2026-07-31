-- 048：以 pending/publishing/ready 状态隔离上传提交不确定性，存量文件保持可见。
IF COL_LENGTH(N'dbo.fn_files_file', N'StorageState') IS NULL
BEGIN
    ALTER TABLE dbo.fn_files_file
        ADD StorageState varchar(16) COLLATE Latin1_General_100_BIN2 NULL;
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
