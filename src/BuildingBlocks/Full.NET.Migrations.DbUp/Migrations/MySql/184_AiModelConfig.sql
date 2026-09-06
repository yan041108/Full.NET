-- 184：AI 模型配置与租户配额表。

CREATE TABLE IF NOT EXISTS fn_ai_model_config
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    TenantId char(36) COLLATE utf8mb4_bin NULL,
    Name varchar(128) NOT NULL,
    ProviderKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    EndpointBaseUrl varchar(512) NOT NULL,
    ModelId varchar(128) COLLATE utf8mb4_bin NOT NULL,
    ApiKeyProtected longtext NULL,
    OrganizationId varchar(128) COLLATE utf8mb4_bin NULL,
    IsDefault tinyint(1) NOT NULL DEFAULT 0,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    LastTestedAtUtc datetime(6) NULL,
    LastTestStatusKey varchar(16) COLLATE utf8mb4_bin NULL,
    LastTestMessage varchar(512) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_model_config_ProviderKey
        CHECK (ProviderKey IN ('openai_compatible', 'ollama')),
    CONSTRAINT CK_fn_ai_model_config_LastTestStatusKey
        CHECK (LastTestStatusKey IS NULL OR LastTestStatusKey IN ('succeeded', 'failed'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_ai_tenant_quota
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    TenantId char(36) COLLATE utf8mb4_bin NOT NULL,
    MonthlyTokenLimit bigint NULL,
    MonthlyRequestLimit bigint NULL,
    UsedTokensThisMonth bigint NOT NULL DEFAULT 0,
    UsedRequestsThisMonth bigint NOT NULL DEFAULT 0,
    QuotaMonthKey varchar(7) COLLATE utf8mb4_bin NOT NULL,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_tenant_quota_MonthlyTokenLimit
        CHECK (MonthlyTokenLimit IS NULL OR MonthlyTokenLimit >= 0),
    CONSTRAINT CK_fn_ai_tenant_quota_MonthlyRequestLimit
        CHECK (MonthlyRequestLimit IS NULL OR MonthlyRequestLimit >= 0),
    CONSTRAINT CK_fn_ai_tenant_quota_UsedTokensThisMonth
        CHECK (UsedTokensThisMonth >= 0),
    CONSTRAINT CK_fn_ai_tenant_quota_UsedRequestsThisMonth
        CHECK (UsedRequestsThisMonth >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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
