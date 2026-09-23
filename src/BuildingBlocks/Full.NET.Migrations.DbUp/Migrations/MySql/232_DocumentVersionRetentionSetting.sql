-- 232：Host 文档版本保留策略数据库覆盖（单行，TenantId IS NULL）。

CREATE TABLE IF NOT EXISTS fn_document_version_retention_setting
(
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    MinimumRetainedVersionsPerItem int NOT NULL COMMENT 'Minimum Retained Versions Per Item',
    MaximumRetainedHistoryVersions int NOT NULL COMMENT 'Maximum Retained History Versions',
    PollSeconds int NOT NULL COMMENT 'Poll Seconds',
    BatchSize int NOT NULL COMMENT 'Batch Size',
    Version bigint NOT NULL COMMENT '乐观并发版本号',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_document_version_retention_setting PRIMARY KEY (Id)
) COMMENT='文档version retention setting表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @indexExists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_document_version_retention_setting'
      AND index_name = 'UX_fn_document_version_retention_setting_Host'
);
SET @ddl := IF(
    @indexExists = 0,
    'CREATE UNIQUE INDEX UX_fn_document_version_retention_setting_Host ON fn_document_version_retention_setting (TenantId)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
