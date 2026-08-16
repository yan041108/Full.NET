-- 048：以 pending/publishing/ready 状态隔离上传提交不确定性，存量文件保持可见。
DROP PROCEDURE IF EXISTS fn_files_upload_state_boundary;
DELIMITER $$
CREATE PROCEDURE fn_files_upload_state_boundary()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_files_file'
          AND COLUMN_NAME = 'StorageState'
    ) THEN
        ALTER TABLE fn_files_file ADD StorageState varchar(16)
                CHARACTER SET ascii COLLATE ascii_bin NULL
                COMMENT '存储状态' AFTER ContentHash;
    END IF;

    UPDATE fn_files_file
    SET StorageState = 'ready'
    WHERE StorageState IS NULL OR StorageState = '';

    ALTER TABLE fn_files_file
        MODIFY COLUMN StorageState varchar(16)
            CHARACTER SET ascii COLLATE ascii_bin NOT NULL;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_files_file'
          AND CONSTRAINT_NAME = 'CK_fn_files_file_StorageState'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_files_file
            DROP CHECK CK_fn_files_file_StorageState;
    END IF;

    ALTER TABLE fn_files_file
        ADD CONSTRAINT CK_fn_files_file_StorageState
            CHECK (StorageState IN ('pending', 'publishing', 'ready'));
END$$
DELIMITER ;

CALL fn_files_upload_state_boundary();
DROP PROCEDURE fn_files_upload_state_boundary;
