-- 192：支付通知幂等记录、退款表，并扩展订单交易状态。

CREATE TABLE IF NOT EXISTS fn_payment_notify_receipt
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    MerchantConfigId char(36) COLLATE utf8mb4_bin NOT NULL,
    ProviderNotifyId varchar(64) COLLATE utf8mb4_bin NOT NULL,
    EventTypeKey varchar(64) COLLATE utf8mb4_bin NOT NULL,
    OutTradeNo varchar(64) COLLATE utf8mb4_bin NULL,
    ProcessStatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    PayloadSummary varchar(512) NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_payment_notify_receipt_ProcessStatusKey
        CHECK (ProcessStatusKey IN (
            'processed',
            'ignored_duplicate',
            'rejected'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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

CREATE TABLE IF NOT EXISTS fn_payment_refund
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    TenantId char(36) COLLATE utf8mb4_bin NOT NULL,
    OrderId char(36) COLLATE utf8mb4_bin NOT NULL,
    MerchantConfigId char(36) COLLATE utf8mb4_bin NOT NULL,
    OutTradeNo varchar(64) COLLATE utf8mb4_bin NOT NULL,
    OutRefundNo varchar(64) COLLATE utf8mb4_bin NOT NULL,
    RefundStateKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    AmountMinor bigint NOT NULL,
    Currency varchar(8) COLLATE utf8mb4_bin NOT NULL,
    Reason varchar(128) NOT NULL,
    ProviderRefundId varchar(64) COLLATE utf8mb4_bin NULL,
    FailMessage varchar(512) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    CompletedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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
