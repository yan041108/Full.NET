-- 194：支付宝 Page Pay 支持——商户配置 ReturnUrl 与渠道键扩展。

IF COL_LENGTH(N'dbo.fn_payment_merchant_config', N'ReturnUrl') IS NULL
    ALTER TABLE dbo.fn_payment_merchant_config
        ADD ReturnUrl nvarchar(512) NULL;

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_merchant_config_ChannelKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_merchant_config'))
    ALTER TABLE dbo.fn_payment_merchant_config DROP CONSTRAINT CK_fn_payment_merchant_config_ChannelKey;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_merchant_config_ChannelKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_merchant_config'))
    ALTER TABLE dbo.fn_payment_merchant_config
        ADD CONSTRAINT CK_fn_payment_merchant_config_ChannelKey
            CHECK (ChannelKey IN (N'wechat_native', N'alipay_page'));

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_order_ChannelKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_order'))
    ALTER TABLE dbo.fn_payment_order DROP CONSTRAINT CK_fn_payment_order_ChannelKey;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_order_ChannelKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_order'))
    ALTER TABLE dbo.fn_payment_order
        ADD CONSTRAINT CK_fn_payment_order_ChannelKey
            CHECK (ChannelKey IN (N'wechat_native', N'alipay_page'));
