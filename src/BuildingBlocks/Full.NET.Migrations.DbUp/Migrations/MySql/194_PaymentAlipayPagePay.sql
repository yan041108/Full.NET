-- 194：支付宝 Page Pay 支持——商户配置 ReturnUrl 与渠道键扩展。

SET @column_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_merchant_config'
      AND COLUMN_NAME = 'ReturnUrl'
);
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_payment_merchant_config ADD ReturnUrl varchar(512) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_merchant_config'
      AND CONSTRAINT_NAME = 'CK_fn_payment_merchant_config_ChannelKey'
      AND CONSTRAINT_TYPE = 'CHECK'
);
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_payment_merchant_config DROP CHECK CK_fn_payment_merchant_config_ChannelKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_merchant_config'
      AND CONSTRAINT_NAME = 'CK_fn_payment_merchant_config_ChannelKey'
      AND CONSTRAINT_TYPE = 'CHECK'
);
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_payment_merchant_config ADD CONSTRAINT CK_fn_payment_merchant_config_ChannelKey CHECK (ChannelKey IN (''wechat_native'', ''alipay_page''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_order'
      AND CONSTRAINT_NAME = 'CK_fn_payment_order_ChannelKey'
      AND CONSTRAINT_TYPE = 'CHECK'
);
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_payment_order DROP CHECK CK_fn_payment_order_ChannelKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_order'
      AND CONSTRAINT_NAME = 'CK_fn_payment_order_ChannelKey'
      AND CONSTRAINT_TYPE = 'CHECK'
);
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_payment_order ADD CONSTRAINT CK_fn_payment_order_ChannelKey CHECK (ChannelKey IN (''wechat_native'', ''alipay_page''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
