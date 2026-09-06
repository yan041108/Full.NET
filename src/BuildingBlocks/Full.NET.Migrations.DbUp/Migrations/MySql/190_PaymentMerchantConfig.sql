-- 190：支付商户配置与支付订单表。

CREATE TABLE IF NOT EXISTS fn_payment_merchant_config
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    TenantId char(36) COLLATE utf8mb4_bin NULL,
    Name varchar(128) NOT NULL,
    ChannelKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    AppId varchar(64) COLLATE utf8mb4_bin NOT NULL,
    MerchantId varchar(32) COLLATE utf8mb4_bin NOT NULL,
    CertificateSerialNo varchar(64) COLLATE utf8mb4_bin NOT NULL,
    NotifyUrl varchar(512) NOT NULL,
    ApiV3KeyProtected longtext NULL,
    PrivateKeyProtected longtext NULL,
    IsDefault tinyint(1) NOT NULL DEFAULT 0,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_payment_merchant_config_ChannelKey
        CHECK (ChannelKey IN ('wechat_native'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_payment_order
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    TenantId char(36) COLLATE utf8mb4_bin NOT NULL,
    MerchantConfigId char(36) COLLATE utf8mb4_bin NOT NULL,
    ChannelKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    OutTradeNo varchar(64) COLLATE utf8mb4_bin NOT NULL,
    TradeStateKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    AmountMinor bigint NOT NULL,
    Currency varchar(8) COLLATE utf8mb4_bin NOT NULL,
    Subject varchar(128) NOT NULL,
    Description varchar(256) NULL,
    CodeUrl varchar(512) NULL,
    ProviderTransactionId varchar(64) COLLATE utf8mb4_bin NULL,
    FailMessage varchar(512) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    PaidAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_payment_order_ChannelKey
        CHECK (ChannelKey IN ('wechat_native')),
    CONSTRAINT CK_fn_payment_order_TradeStateKey
        CHECK (TradeStateKey IN (
            'created',
            'awaiting_payment',
            'succeeded',
            'closed',
            'failed')),
    CONSTRAINT CK_fn_payment_order_AmountMinor
        CHECK (AmountMinor > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_merchant_config'
      AND INDEX_NAME = 'IX_fn_payment_merchant_config_TenantId_ChannelKey'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_payment_merchant_config_TenantId_ChannelKey ON fn_payment_merchant_config (TenantId, ChannelKey, IsEnabled, Name, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_merchant_config'
      AND INDEX_NAME = 'IX_fn_payment_merchant_config_IsEnabled_Name'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_payment_merchant_config_IsEnabled_Name ON fn_payment_merchant_config (IsEnabled, Name, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_order'
      AND INDEX_NAME = 'UX_fn_payment_order_OutTradeNo'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_payment_order_OutTradeNo ON fn_payment_order (OutTradeNo)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_order'
      AND INDEX_NAME = 'IX_fn_payment_order_TenantId_CreatedAtUtc'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_payment_order_TenantId_CreatedAtUtc ON fn_payment_order (TenantId, CreatedAtUtc DESC, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
