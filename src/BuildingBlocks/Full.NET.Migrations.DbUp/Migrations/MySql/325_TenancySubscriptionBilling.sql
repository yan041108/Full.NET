-- 冻结业务报价与资金身份；不对其他模块表建立外键。
CREATE TABLE IF NOT EXISTS fn_tenancy_package_offer (
    Id binary(16) NOT NULL COMMENT '复用套餐逻辑主键',
    PaymentTenantId binary(16) NOT NULL COMMENT '拥有收款商户的租户',
    MerchantConfigId binary(16) NOT NULL COMMENT '冻结收款商户配置',
    AmountMinor bigint NOT NULL COMMENT '最小货币单位金额',
    Currency varchar(3) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '规范大写币种',
    SeatLimit bigint NOT NULL COMMENT '席位限额',
    StorageLimit bigint NOT NULL COMMENT '存储字节限额',
    DurationDays int NOT NULL COMMENT '价格对应的有效天数',
    IsActive tinyint(1) NOT NULL COMMENT '是否允许创建新报价',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '最近更新时间 UTC',
    Version int NOT NULL COMMENT '报价乐观并发版本',
    CONSTRAINT PK_fn_tenancy_package_offer PRIMARY KEY (Id)
) COMMENT='可发布的套餐收款报价与限额' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_tenancy_offer_entitlement (
    Id binary(16) NOT NULL COMMENT '映射逻辑 UUID v7',
    OfferId binary(16) NOT NULL COMMENT '报价所属套餐',
    EntitlementId binary(16) NOT NULL COMMENT '受控权益目录标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
    CONSTRAINT PK_fn_tenancy_offer_entitlement PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_offer_entitlement_OfferId_EntitlementId (OfferId,EntitlementId)
) COMMENT='报价允许授予的功能目录映射' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_tenancy_subscription_invoice (
    Id binary(16) NOT NULL COMMENT '复用订阅逻辑主键',
    TenantId binary(16) NOT NULL COMMENT '权益受益租户',
    RequestId binary(16) NOT NULL COMMENT '调用方固定创建身份',
    RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '冻结购买输入摘要',
    PackageId binary(16) NOT NULL COMMENT '冻结套餐标识',
    PackageVersion int NOT NULL COMMENT '冻结套餐元数据版本',
    OfferVersion int NOT NULL COMMENT '冻结报价版本',
    PaymentTenantId binary(16) NOT NULL COMMENT '拥有收款商户的租户',
    MerchantConfigId binary(16) NOT NULL COMMENT '冻结收款商户',
    PaymentOperationId binary(16) NOT NULL COMMENT '原始支付创建幂等身份',
    PaymentOrderId binary(16) NULL COMMENT '已验证的实际支付订单',
    ProviderTransactionId varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '已验证的渠道交易标识',
    AmountMinor bigint NOT NULL COMMENT '冻结最小单位金额',
    Currency varchar(3) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '冻结规范币种',
    SeatLimit bigint NOT NULL COMMENT '冻结席位限额',
    StorageLimit bigint NOT NULL COMMENT '冻结存储限额',
    EffectiveFromUtc datetime(6) NOT NULL COMMENT '权益生效时间 UTC',
    EffectiveToUtc datetime(6) NOT NULL COMMENT '权益到期时间 UTC',
    Phase varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '待付款已履约退款或核对阶段',
    LastRefundId binary(16) NULL COMMENT '最近已接收的成功退款身份',
    RefundAmountMinor bigint NOT NULL COMMENT '最近已验证退款金额',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '最近更新时间 UTC',
    Version int NOT NULL COMMENT '接收状态乐观并发版本',
    Subject varchar(128) NOT NULL COMMENT '冻结商品名称',
    CONSTRAINT PK_fn_tenancy_subscription_invoice PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_subscription_invoice_TenantId_RequestId (TenantId,RequestId),
    UNIQUE KEY UX_fn_tenancy_subscription_invoice_PaymentOperationId (PaymentOperationId),
    KEY IX_fn_tenancy_subscription_invoice_TenantId_CreatedAtUtc (TenantId,CreatedAtUtc)
) COMMENT='订阅不可变报价及资金事实接收状态' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_tenancy_invoice_entitlement (
    Id binary(16) NOT NULL COMMENT '快照逻辑 UUID v7',
    InvoiceId binary(16) NOT NULL COMMENT '原始订阅报价',
    EntitlementId binary(16) NOT NULL COMMENT '冻结权益目录标识',
    EntitlementCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '冻结稳定功能码',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
    CONSTRAINT PK_fn_tenancy_invoice_entitlement PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_invoice_entitlement_InvoiceId_EntitlementId (InvoiceId,EntitlementId)
) COMMENT='报价时冻结的功能权益快照' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_tenancy_ensure_subscription_origin;
DELIMITER $$
CREATE PROCEDURE fn_tenancy_ensure_subscription_origin()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_tenancy_tenant_entitlement_binding' AND COLUMN_NAME='SourceSubscriptionId') THEN
        ALTER TABLE fn_tenancy_tenant_entitlement_binding ADD COLUMN SourceSubscriptionId binary(16) NULL COMMENT '自动授权的来源订阅；空值为独立人工授权';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_tenancy_quota_metric' AND COLUMN_NAME='SourceSubscriptionId') THEN
        ALTER TABLE fn_tenancy_quota_metric ADD COLUMN SourceSubscriptionId binary(16) NULL COMMENT '提供本限额的来源订阅；空值为独立人工配置';
    END IF;
END$$
DELIMITER ;
CALL fn_tenancy_ensure_subscription_origin();
DROP PROCEDURE IF EXISTS fn_tenancy_ensure_subscription_origin;
