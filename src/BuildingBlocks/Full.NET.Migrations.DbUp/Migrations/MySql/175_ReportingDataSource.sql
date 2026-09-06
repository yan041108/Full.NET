-- 175：Reporting 报表数据源配置主表。

CREATE TABLE IF NOT EXISTS fn_reporting_data_source
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    TenantId char(36) COLLATE utf8mb4_bin NULL,
    Name varchar(128) NOT NULL,
    ProviderKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    ServerHost varchar(256) NOT NULL,
    Port int NOT NULL,
    DatabaseName varchar(128) NOT NULL,
    Username varchar(128) NOT NULL,
    PasswordProtected longtext NOT NULL,
    TrustServerCertificate tinyint(1) NOT NULL DEFAULT 0,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    LastTestedAtUtc datetime(6) NULL,
    LastTestStatusKey varchar(16) COLLATE utf8mb4_bin NULL,
    LastTestMessage varchar(512) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_reporting_data_source_ProviderKey
        CHECK (ProviderKey IN ('sql_server', 'mysql')),
    CONSTRAINT CK_fn_reporting_data_source_LastTestStatusKey
        CHECK (LastTestStatusKey IS NULL OR LastTestStatusKey IN ('succeeded', 'failed'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_reporting_data_source'
      AND INDEX_NAME = 'IX_fn_reporting_data_source_IsEnabled_Name'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_reporting_data_source_IsEnabled_Name ON fn_reporting_data_source (IsEnabled, Name, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_reporting_data_source'
      AND INDEX_NAME = 'IX_fn_reporting_data_source_TenantId'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_reporting_data_source_TenantId ON fn_reporting_data_source (TenantId, Name, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
