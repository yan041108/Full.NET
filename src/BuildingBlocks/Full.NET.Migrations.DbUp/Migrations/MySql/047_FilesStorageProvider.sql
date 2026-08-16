-- 047：持久化文件存储 Provider，并把对象键唯一性限定在 Provider 内。
DROP PROCEDURE IF EXISTS fn_files_storage_provider_boundary;
DELIMITER $$
CREATE PROCEDURE fn_files_storage_provider_boundary()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_files_file'
          AND COLUMN_NAME = 'ProviderKey'
    ) THENALTER TABLE fn_files_file ADD ProviderKey varchar(64)
                CHARACTER SET ascii COLLATE ascii_bin NULL
                AFTER SizeBytes COMMENT '存储提供程序键'
    END IF;

    UPDATE fn_files_file
    SET ProviderKey = 'local'
    WHERE ProviderKey IS NULL OR ProviderKey = '';

    ALTER TABLE fn_files_file
        MODIFY COLUMN ProviderKey varchar(64)
            CHARACTER SET ascii COLLATE ascii_bin NOT NULL;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_files_file'
          AND INDEX_NAME = 'UX_fn_files_file_StorageKey'
    ) THEN
        ALTER TABLE fn_files_file
            DROP INDEX UX_fn_files_file_StorageKey;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_files_file'
          AND INDEX_NAME = 'UX_fn_files_file_ProviderKey_StorageKey'
    ) THEN
        ALTER TABLE fn_files_file
            ADD UNIQUE INDEX UX_fn_files_file_ProviderKey_StorageKey
                (ProviderKey, StorageKey);
    END IF;
END$$
DELIMITER ;

CALL fn_files_storage_provider_boundary();
DROP PROCEDURE fn_files_storage_provider_boundary;
