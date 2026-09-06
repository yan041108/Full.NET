-- 192：支付通知幂等记录、退款表，并扩展订单交易状态。

IF OBJECT_ID(N'dbo.fn_payment_notify_receipt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_payment_notify_receipt
    (
        Id uniqueidentifier NOT NULL,
        MerchantConfigId uniqueidentifier NOT NULL,
        ProviderNotifyId varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        EventTypeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OutTradeNo varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        ProcessStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PayloadSummary nvarchar(512) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_payment_notify_receipt PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_payment_notify_receipt_ProcessStatusKey
            CHECK (ProcessStatusKey IN (
                N'processed',
                N'ignored_duplicate',
                N'rejected'))
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
      AND name = N'UX_fn_payment_notify_receipt_MerchantConfigId_ProviderNotifyId')
    CREATE UNIQUE INDEX UX_fn_payment_notify_receipt_MerchantConfigId_ProviderNotifyId
        ON dbo.fn_payment_notify_receipt(MerchantConfigId, ProviderNotifyId);

IF OBJECT_ID(N'dbo.fn_payment_refund', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_payment_refund
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OrderId uniqueidentifier NOT NULL,
        MerchantConfigId uniqueidentifier NOT NULL,
        OutTradeNo varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OutRefundNo varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RefundStateKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AmountMinor bigint NOT NULL,
        Currency varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Reason nvarchar(128) NOT NULL,
        ProviderRefundId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        FailMessage nvarchar(512) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_payment_refund_Version DEFAULT (1),
        CONSTRAINT PK_fn_payment_refund PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_payment_refund_RefundStateKey
            CHECK (RefundStateKey IN (
                N'created',
                N'processing',
                N'succeeded',
                N'failed',
                N'closed')),
        CONSTRAINT CK_fn_payment_refund_AmountMinor
            CHECK (AmountMinor > 0)
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_payment_refund')
      AND name = N'UX_fn_payment_refund_OutRefundNo')
    CREATE UNIQUE INDEX UX_fn_payment_refund_OutRefundNo
        ON dbo.fn_payment_refund(OutRefundNo);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_payment_refund')
      AND name = N'IX_fn_payment_refund_TenantId_CreatedAtUtc')
    CREATE INDEX IX_fn_payment_refund_TenantId_CreatedAtUtc
        ON dbo.fn_payment_refund(TenantId, CreatedAtUtc DESC, Id);

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_order_TradeStateKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_order'))
    ALTER TABLE dbo.fn_payment_order DROP CONSTRAINT CK_fn_payment_order_TradeStateKey;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_order_TradeStateKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_order'))
    ALTER TABLE dbo.fn_payment_order
        ADD CONSTRAINT CK_fn_payment_order_TradeStateKey
            CHECK (TradeStateKey IN (
                N'created',
                N'awaiting_payment',
                N'succeeded',
                N'closed',
                N'failed',
                N'refunding',
                N'refunded'));
