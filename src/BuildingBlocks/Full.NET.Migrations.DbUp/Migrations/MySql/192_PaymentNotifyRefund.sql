-- 192：支付通知幂等记录、退款表，并扩展订单交易状态。

CREATE TABLE IF NOT EXISTS fn_payment_notify_receipt (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    MerchantConfigId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '商户配置标识',
    ProviderNotifyId varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '渠道通知标识',
    EventTypeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '事件类型键',
    OutTradeNo varchar(64) COLLATE utf8mb4_bin NULL COMMENT '商户订单号',
    ProcessStatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '回执处理状态键',
    PayloadSummary varchar(512) NOT NULL COMMENT '载荷摘要',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_payment_notify_receipt_ProcessStatusKey
        CHECK (ProcessStatusKey IN (
            'processed',
            'ignored_duplicate',
            'rejected'))
) COMMENT='支付渠道回执表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_notify_receipt'
      AND INDEX_NAME = 'UX_fn_payment_notify_receipt_MerchantConfigId_ProviderNotifyId'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_payment_notify_receipt_MerchantConfigId_ProviderNotifyId ON fn_payment_notify_receipt (MerchantConfigId, ProviderNotifyId)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

CREATE TABLE IF NOT EXISTS fn_payment_refund (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    TenantId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    OrderId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '订单标识',
    MerchantConfigId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '商户配置标识',
    OutTradeNo varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '商户订单号',
    OutRefundNo varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '商户退款号',
    RefundStateKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '退款状态键',
    AmountMinor bigint NOT NULL COMMENT '金额(最小货币单位)',
    Currency varchar(8) COLLATE utf8mb4_bin NOT NULL COMMENT '币种',
    Reason varchar(128) NOT NULL COMMENT '原因说明',
    ProviderRefundId varchar(64) COLLATE utf8mb4_bin NULL COMMENT '渠道退款标识',
    FailMessage varchar(512) NULL COMMENT '失败消息',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_payment_refund_RefundStateKey
        CHECK (RefundStateKey IN (
            'created',
            'processing',
            'succeeded',
            'failed',
            'closed')),
    CONSTRAINT CK_fn_payment_refund_AmountMinor
        CHECK (AmountMinor > 0)
) COMMENT='支付退款表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_refund'
      AND INDEX_NAME = 'UX_fn_payment_refund_OutRefundNo'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_payment_refund_OutRefundNo ON fn_payment_refund (OutRefundNo)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_refund'
      AND INDEX_NAME = 'IX_fn_payment_refund_TenantId_CreatedAtUtc'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_payment_refund_TenantId_CreatedAtUtc ON fn_payment_refund (TenantId, CreatedAtUtc DESC, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_order'
      AND CONSTRAINT_NAME = 'CK_fn_payment_order_TradeStateKey'
      AND CONSTRAINT_TYPE = 'CHECK'
);
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_payment_order DROP CHECK CK_fn_payment_order_TradeStateKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_order'
      AND CONSTRAINT_NAME = 'CK_fn_payment_order_TradeStateKey'
      AND CONSTRAINT_TYPE = 'CHECK'
);
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_payment_order ADD CONSTRAINT CK_fn_payment_order_TradeStateKey CHECK (TradeStateKey IN (''created'', ''awaiting_payment'', ''succeeded'', ''closed'', ''failed'', ''refunding'', ''refunded''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
