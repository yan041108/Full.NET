-- 190：支付商户配置与支付订单表。

CREATE TABLE IF NOT EXISTS fn_payment_merchant_config (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    TenantId char(36) COLLATE utf8mb4_bin NULL COMMENT '租户标识；NULL 表示 Host 级',
    Name varchar(128) NOT NULL COMMENT '名称',
    ChannelKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '渠道键',
    AppId varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '应用标识',
    MerchantId varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '商户标识',
    CertificateSerialNo varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '证书序列号',
    NotifyUrl varchar(512) NOT NULL COMMENT '通知回调地址',
    ApiV3KeyProtected longtext NULL COMMENT '受保护的 API v3 密钥',
    PrivateKeyProtected longtext NULL COMMENT '受保护的私钥',
    IsDefault tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否默认',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_payment_merchant_config_ChannelKey
        CHECK (ChannelKey IN ('wechat_native'))
) COMMENT='支付商户配置表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_payment_order (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    TenantId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    MerchantConfigId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '商户配置标识',
    ChannelKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '渠道键',
    OutTradeNo varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '商户订单号',
    TradeStateKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '交易状态键',
    AmountMinor bigint NOT NULL COMMENT '金额(最小货币单位)',
    Currency varchar(8) COLLATE utf8mb4_bin NOT NULL COMMENT '币种',
    Subject varchar(128) NOT NULL COMMENT '主题',
    Description varchar(256) NULL COMMENT '描述',
    CodeUrl varchar(512) NULL COMMENT '付款码地址',
    ProviderTransactionId varchar(64) COLLATE utf8mb4_bin NULL COMMENT '渠道交易标识',
    FailMessage varchar(512) NULL COMMENT '失败消息',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    PaidAtUtc datetime(6) NULL COMMENT 'Paid At(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
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
) COMMENT='支付订单表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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
