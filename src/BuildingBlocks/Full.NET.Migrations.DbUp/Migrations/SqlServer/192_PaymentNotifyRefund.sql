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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'支付渠道回执表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_notify_receipt'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_notify_receipt'), N'EventTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt', @level2type=N'COLUMN', @level2name=N'EventTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_notify_receipt'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_notify_receipt'), N'MerchantConfigId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'商户配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt', @level2type=N'COLUMN', @level2name=N'MerchantConfigId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_notify_receipt'), N'OutTradeNo', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'商户订单号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt', @level2type=N'COLUMN', @level2name=N'OutTradeNo';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_notify_receipt'), N'PayloadSummary', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'载荷摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt', @level2type=N'COLUMN', @level2name=N'PayloadSummary';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_notify_receipt'), N'ProcessStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'回执处理状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt', @level2type=N'COLUMN', @level2name=N'ProcessStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_notify_receipt'), N'ProviderNotifyId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道通知标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_notify_receipt', @level2type=N'COLUMN', @level2name=N'ProviderNotifyId';
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'支付退款表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'AmountMinor', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'金额(最小货币单位)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'AmountMinor';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'Currency', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'币种', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'Currency';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'FailMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'失败消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'FailMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'MerchantConfigId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'商户配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'MerchantConfigId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'OrderId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'订单标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'OrderId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'OutRefundNo', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'商户退款号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'OutRefundNo';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'OutTradeNo', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'商户订单号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'OutTradeNo';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'ProviderRefundId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道退款标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'ProviderRefundId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'Reason', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原因说明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'Reason';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'RefundStateKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'退款状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'RefundStateKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_refund')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_refund'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_refund', @level2type=N'COLUMN', @level2name=N'Version';
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
