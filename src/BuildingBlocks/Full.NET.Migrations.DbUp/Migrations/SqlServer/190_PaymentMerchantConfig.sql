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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'支付商户配置表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'ApiV3KeyProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的 API v3 密钥', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'ApiV3KeyProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'AppId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'AppId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'CertificateSerialNo', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'证书序列号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'CertificateSerialNo';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'ChannelKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'ChannelKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'IsDefault', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否默认', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'IsDefault';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'MerchantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'商户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'MerchantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'NotifyUrl', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知回调地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'NotifyUrl';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'PrivateKeyProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的私钥', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'PrivateKeyProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_merchant_config'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_merchant_config', @level2type=N'COLUMN', @level2name=N'Version';
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'支付订单表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'AmountMinor', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'金额(最小货币单位)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'AmountMinor';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'ChannelKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'ChannelKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'CodeUrl', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'付款码地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'CodeUrl';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'Currency', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'币种', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'Currency';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'FailMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'失败消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'FailMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'MerchantConfigId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'商户配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'MerchantConfigId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'OutTradeNo', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'商户订单号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'OutTradeNo';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'PaidAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Paid At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'PaidAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'ProviderTransactionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道交易标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'ProviderTransactionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'Subject', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'主题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'Subject';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'TradeStateKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'交易状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'TradeStateKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_payment_order')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_order'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_payment_order', @level2type=N'COLUMN', @level2name=N'Version';
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
