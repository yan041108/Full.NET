-- 175：Reporting 报表数据源配置主表。

CREATE TABLE IF NOT EXISTS fn_reporting_data_source (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    TenantId char(36) COLLATE utf8mb4_bin NULL COMMENT '租户标识；NULL 表示 Host 级',
    Name varchar(128) NOT NULL COMMENT '名称',
    ProviderKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '存储提供程序键',
    ServerHost varchar(256) NOT NULL COMMENT '服务器主机',
    Port int NOT NULL COMMENT '端口',
    DatabaseName varchar(128) NOT NULL COMMENT '数据库名',
    Username varchar(128) NOT NULL COMMENT '用户名',
    PasswordProtected longtext NOT NULL COMMENT '受保护的密码',
    TrustServerCertificate tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否信任服务器证书',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    LastTestedAtUtc datetime(6) NULL COMMENT 'Last Tested At(UTC)',
    LastTestStatusKey varchar(16) COLLATE utf8mb4_bin NULL COMMENT '最近一次探测状态键',
    LastTestMessage varchar(512) NULL COMMENT '最近一次探测消息',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_reporting_data_source_ProviderKey
        CHECK (ProviderKey IN ('sql_server', 'mysql')),
    CONSTRAINT CK_fn_reporting_data_source_LastTestStatusKey
        CHECK (LastTestStatusKey IS NULL OR LastTestStatusKey IN ('succeeded', 'failed'))
) COMMENT='报表数据源表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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
