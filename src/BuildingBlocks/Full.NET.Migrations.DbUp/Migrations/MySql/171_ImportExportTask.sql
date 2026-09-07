-- 171：ImportExport 静态 Schema 导入任务表。

CREATE TABLE IF NOT EXISTS fn_import_export_task (
    Id char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '逻辑主键',
    TenantId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    SchemaKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '结构键',
    SchemaDisplayName varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '结构显示名',
    WorksheetKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '工作表键',
    SourceFileId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '源文件标识',
    SourceFileName varchar(260) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '源文件名',
    StatusKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '状态键',
    TotalRows int NOT NULL COMMENT '总行数',
    ValidRowCount int NOT NULL COMMENT '有效行数',
    InvalidRowCount int NOT NULL COMMENT '无效行数',
    PreviewRowsJson longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT 'Preview Rows(JSON)',
    ErrorCode varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '错误码',
    RequestedByUserId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT '请求人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    PreviewCompletedAtUtc datetime(6) NULL COMMENT 'Preview Completed At(UTC)',
    Version bigint NOT NULL COMMENT '乐观并发版本号',
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
