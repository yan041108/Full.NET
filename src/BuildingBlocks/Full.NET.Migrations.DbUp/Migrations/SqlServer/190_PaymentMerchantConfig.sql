-- 190：支付商户配置与支付订单表。

IF OBJECT_ID(N'dbo.fn_payment_merchant_config', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_payment_merchant_config
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        Name nvarchar(128) NOT NULL,
        ChannelKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AppId varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        MerchantId varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CertificateSerialNo varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        NotifyUrl nvarchar(512) NOT NULL,
        ApiV3KeyProtected nvarchar(max) NULL,
        PrivateKeyProtected nvarchar(max) NULL,
        IsDefault bit NOT NULL
            CONSTRAINT DF_fn_payment_merchant_config_IsDefault DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_payment_merchant_config_IsEnabled DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_payment_merchant_config_Version DEFAULT (1),
        CONSTRAINT PK_fn_payment_merchant_config PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_payment_merchant_config_ChannelKey
            CHECK (ChannelKey IN (N'wechat_native'))
    );
END;

IF OBJECT_ID(N'dbo.fn_payment_order', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_payment_order
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        MerchantConfigId uniqueidentifier NOT NULL,
        ChannelKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OutTradeNo varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TradeStateKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AmountMinor bigint NOT NULL,
        Currency varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Subject nvarchar(128) NOT NULL,
        Description nvarchar(256) NULL,
        CodeUrl nvarchar(512) NULL,
        ProviderTransactionId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        FailMessage nvarchar(512) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        PaidAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_payment_order_Version DEFAULT (1),
        CONSTRAINT PK_fn_payment_order PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_payment_order_ChannelKey
            CHECK (ChannelKey IN (N'wechat_native')),
        CONSTRAINT CK_fn_payment_order_TradeStateKey
            CHECK (TradeStateKey IN (
                N'created',
                N'awaiting_payment',
                N'succeeded',
                N'closed',
                N'failed')),
        CONSTRAINT CK_fn_payment_order_AmountMinor
            CHECK (AmountMinor > 0)
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
      AND name = N'IX_fn_payment_merchant_config_TenantId_ChannelKey')
    CREATE INDEX IX_fn_payment_merchant_config_TenantId_ChannelKey
        ON dbo.fn_payment_merchant_config(TenantId, ChannelKey, IsEnabled, Name, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
      AND name = N'IX_fn_payment_merchant_config_IsEnabled_Name')
    CREATE INDEX IX_fn_payment_merchant_config_IsEnabled_Name
        ON dbo.fn_payment_merchant_config(IsEnabled, Name, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_payment_order')
      AND name = N'UX_fn_payment_order_OutTradeNo')
    CREATE UNIQUE INDEX UX_fn_payment_order_OutTradeNo
        ON dbo.fn_payment_order(OutTradeNo);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_payment_order')
      AND name = N'IX_fn_payment_order_TenantId_CreatedAtUtc')
    CREATE INDEX IX_fn_payment_order_TenantId_CreatedAtUtc
        ON dbo.fn_payment_order(TenantId, CreatedAtUtc DESC, Id);
