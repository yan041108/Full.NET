-- 184：AI 模型配置与租户配额表。

CREATE TABLE IF NOT EXISTS fn_ai_model_config (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    TenantId char(36) COLLATE utf8mb4_bin NULL COMMENT '租户标识；NULL 表示 Host 级',
    Name varchar(128) NOT NULL COMMENT '名称',
    ProviderKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '存储提供程序键',
    EndpointBaseUrl varchar(512) NOT NULL COMMENT '接口基础地址',
    ModelId varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '模型标识',
    ApiKeyProtected longtext NULL COMMENT '受保护的 API 密钥',
    OrganizationId varchar(128) COLLATE utf8mb4_bin NULL COMMENT '组织标识',
    IsDefault tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否默认',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    LastTestedAtUtc datetime(6) NULL COMMENT 'Last Tested At(UTC)',
    LastTestStatusKey varchar(16) COLLATE utf8mb4_bin NULL COMMENT '最近一次探测状态键',
    LastTestMessage varchar(512) NULL COMMENT '最近一次探测消息',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_model_config_ProviderKey
        CHECK (ProviderKey IN ('openai_compatible', 'ollama')),
    CONSTRAINT CK_fn_ai_model_config_LastTestStatusKey
        CHECK (LastTestStatusKey IS NULL OR LastTestStatusKey IN ('succeeded', 'failed'))
) COMMENT='人工智能模型配置表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_ai_tenant_quota (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    TenantId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    MonthlyTokenLimit bigint NULL COMMENT '每月 Token 上限',
    MonthlyRequestLimit bigint NULL COMMENT '每月请求上限',
    UsedTokensThisMonth bigint NOT NULL DEFAULT 0 COMMENT '本月已用 Token 数',
    UsedRequestsThisMonth bigint NOT NULL DEFAULT 0 COMMENT '本月已用请求数',
    QuotaMonthKey varchar(7) COLLATE utf8mb4_bin NOT NULL COMMENT '配额月份键',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_tenant_quota_MonthlyTokenLimit
        CHECK (MonthlyTokenLimit IS NULL OR MonthlyTokenLimit >= 0),
    CONSTRAINT CK_fn_ai_tenant_quota_MonthlyRequestLimit
        CHECK (MonthlyRequestLimit IS NULL OR MonthlyRequestLimit >= 0),
    CONSTRAINT CK_fn_ai_tenant_quota_UsedTokensThisMonth
        CHECK (UsedTokensThisMonth >= 0),
    CONSTRAINT CK_fn_ai_tenant_quota_UsedRequestsThisMonth
        CHECK (UsedRequestsThisMonth >= 0)
) COMMENT='人工智能租户配额表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_model_config'
      AND INDEX_NAME = 'IX_fn_ai_model_config_IsEnabled_Name'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_ai_model_config_IsEnabled_Name ON fn_ai_model_config (IsEnabled, Name, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_model_config'
      AND INDEX_NAME = 'IX_fn_ai_model_config_TenantId'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_ai_model_config_TenantId ON fn_ai_model_config (TenantId, Name, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_tenant_quota'
      AND INDEX_NAME = 'UX_fn_ai_tenant_quota_TenantId'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_ai_tenant_quota_TenantId ON fn_ai_tenant_quota (TenantId)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
