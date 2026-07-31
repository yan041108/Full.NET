-- 047：持久化文件存储 Provider，并把对象键唯一性限定在 Provider 内。
IF COL_LENGTH(N'dbo.fn_files_file', N'ProviderKey') IS NULL
BEGIN
    EXEC(N'
        ALTER TABLE dbo.fn_files_file
            ADD ProviderKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL;
    ');
END;

EXEC(N'
    UPDATE dbo.fn_files_file
    SET ProviderKey = ''local''
    WHERE ProviderKey IS NULL OR ProviderKey = '''';

    ALTER TABLE dbo.fn_files_file
        ALTER COLUMN ProviderKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL;
');

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_files_file')
      AND name = N'UX_fn_files_file_StorageKey'
)
BEGIN
    DROP INDEX UX_fn_files_file_StorageKey
        ON dbo.fn_files_file;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_files_file')
      AND name = N'UX_fn_files_file_ProviderKey_StorageKey'
)
BEGIN
    DROP INDEX UX_fn_files_file_ProviderKey_StorageKey
        ON dbo.fn_files_file;
END;

EXEC(N'
    CREATE UNIQUE INDEX UX_fn_files_file_ProviderKey_StorageKey
        ON dbo.fn_files_file(ProviderKey, StorageKey);
');
