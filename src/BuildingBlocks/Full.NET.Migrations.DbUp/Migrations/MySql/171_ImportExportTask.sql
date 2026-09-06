-- 171：ImportExport 静态 Schema 导入任务表。

CREATE TABLE IF NOT EXISTS fn_import_export_task
(
    Id char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    TenantId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    SchemaKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    SchemaDisplayName varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    WorksheetKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    SourceFileId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    SourceFileName varchar(260) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL,
    StatusKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    TotalRows int NOT NULL,
    ValidRowCount int NOT NULL,
    InvalidRowCount int NOT NULL,
    PreviewRowsJson longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL,
    ErrorCode varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL,
    RequestedByUserId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    PreviewCompletedAtUtc datetime(6) NULL,
    Version bigint NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_import_export_task_StatusKey
        CHECK (StatusKey IN ('uploaded', 'preview_succeeded', 'preview_failed'))
) COMMENT='ImportExport 静态 Schema 导入任务表';

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_import_export_task'
      AND index_name = 'IX_fn_import_export_task_TenantId_CreatedAtUtc'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE INDEX IX_fn_import_export_task_TenantId_CreatedAtUtc ON fn_import_export_task (TenantId, CreatedAtUtc, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_import_export_task'
      AND index_name = 'IX_fn_import_export_task_SchemaKey'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE INDEX IX_fn_import_export_task_SchemaKey ON fn_import_export_task (TenantId, SchemaKey, CreatedAtUtc)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
