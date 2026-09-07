-- 130：Host 虚拟目录与文件元数据修订号；目录仅表达逻辑归属，不映射磁盘路径。

CREATE TABLE IF NOT EXISTS fn_files_folder (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ParentId BINARY(16) NULL COMMENT '父级标识',
    Name varchar(128) NOT NULL COMMENT '名称',
    DisplayOrder int NOT NULL DEFAULT 0 COMMENT '显示顺序',
    Revision bigint NOT NULL DEFAULT 0 COMMENT '修订号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    UpdatedByUserId BINARY(16) NULL COMMENT '更新人用户标识',
    DeletedAtUtc datetime(6) NULL COMMENT '删除时间(UTC)',
    PRIMARY KEY (Id)
) COMMENT='文件虚拟目录表';

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
