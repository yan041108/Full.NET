-- 两个 DDL 步骤独立探测，隐式提交中断后能够收敛。
DROP PROCEDURE IF EXISTS fn_migrate_files_upload_operation;
DELIMITER $$
CREATE PROCEDURE fn_migrate_files_upload_operation()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_files_file' AND COLUMN_NAME = 'UploadOperationKey') THEN
        ALTER TABLE fn_files_file ADD UploadOperationKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '受控上传操作幂等键，未知发布结果禁止盲目重传';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_files_file' AND INDEX_NAME = 'UX_fn_files_file_UploadOperationKey') THEN
        CREATE UNIQUE INDEX UX_fn_files_file_UploadOperationKey ON fn_files_file (UploadOperationKey);
    END IF;
END$$
DELIMITER ;
CALL fn_migrate_files_upload_operation();
DROP PROCEDURE IF EXISTS fn_migrate_files_upload_operation;
