-- 130：Host 虚拟目录与文件元数据修订号；目录仅表达逻辑归属，不映射磁盘路径。

CREATE TABLE IF NOT EXISTS fn_files_folder
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    ParentId BINARY(16) NULL,
    Name varchar(128) NOT NULL,
    DisplayOrder int NOT NULL DEFAULT 0,
    Revision bigint NOT NULL DEFAULT 0,
    CreatedAtUtc datetime(6) NOT NULL,
    CreatedByUserId BINARY(16) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    UpdatedByUserId BINARY(16) NULL,
    DeletedAtUtc datetime(6) NULL,
    PRIMARY KEY (Id)
);

SET @index_exists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_files_folder'
      AND index_name = 'IX_fn_files_folder_ParentId');
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_files_folder_ParentId ON fn_files_folder (ParentId, DisplayOrder, Name)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_files_folder'
      AND index_name = 'UX_fn_files_folder_ParentId_Name');
SET @ddl := IF(
    @index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_files_folder_ParentId_Name ON fn_files_folder (ParentId, Name)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

ALTER TABLE fn_files_file
    ADD COLUMN IF NOT EXISTS FolderId BINARY(16) NULL,
    ADD COLUMN IF NOT EXISTS Revision bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS UpdatedAtUtc datetime(6) NULL,
    ADD COLUMN IF NOT EXISTS UpdatedByUserId BINARY(16) NULL;

SET @index_exists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_files_file'
      AND index_name = 'IX_fn_files_file_FolderId');
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_files_file_FolderId ON fn_files_file (FolderId, CreatedAtUtc, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
