-- 冻结业务报价与资金身份；不对其他模块表建立外键。
IF OBJECT_ID(N'dbo.fn_tenancy_package_offer',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_package_offer (
    Id uniqueidentifier NOT NULL,
    PaymentTenantId uniqueidentifier NOT NULL,
    MerchantConfigId uniqueidentifier NOT NULL,
    AmountMinor bigint NOT NULL,
    Currency varchar(3) NOT NULL,
    SeatLimit bigint NOT NULL,
    StorageLimit bigint NOT NULL,
    DurationDays int NOT NULL,
    IsActive bit NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    UpdatedAtUtc datetimeoffset(7) NOT NULL,
    Version int NOT NULL,
    CONSTRAINT PK_fn_tenancy_package_offer PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可发布的套餐收款报价与限额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用套餐逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'PaymentTenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'拥有收款商户的租户',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'PaymentTenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'MerchantConfigId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结收款商户配置',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'MerchantConfigId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'AmountMinor','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最小货币单位金额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'AmountMinor';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'Currency','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'规范大写币种',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'Currency';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'SeatLimit','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'席位限额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'SeatLimit';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'StorageLimit','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'存储字节限额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'StorageLimit';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'IsActive','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'是否允许创建新报价',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'IsActive';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近更新时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'报价乐观并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_package_offer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_package_offer'),N'DurationDays','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'价格对应的有效天数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_package_offer',@level2type=N'COLUMN',@level2name=N'DurationDays';
IF OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_offer_entitlement (
    Id uniqueidentifier NOT NULL,
    OfferId uniqueidentifier NOT NULL,
    EntitlementId uniqueidentifier NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_tenancy_offer_entitlement PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement') AND name=N'UX_fn_tenancy_offer_entitlement_OfferId_EntitlementId')
    CREATE UNIQUE INDEX UX_fn_tenancy_offer_entitlement_OfferId_EntitlementId ON dbo.fn_tenancy_offer_entitlement(OfferId,EntitlementId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'报价允许授予的功能目录映射',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_offer_entitlement';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'映射逻辑 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_offer_entitlement',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement'),N'OfferId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'报价所属套餐',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_offer_entitlement',@level2type=N'COLUMN',@level2name=N'OfferId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement'),N'EntitlementId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'受控权益目录标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_offer_entitlement',@level2type=N'COLUMN',@level2name=N'EntitlementId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_offer_entitlement'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_offer_entitlement',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_subscription_invoice (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    RequestId uniqueidentifier NOT NULL,
    RequestHash varchar(64) NOT NULL,
    PackageId uniqueidentifier NOT NULL,
    PackageVersion int NOT NULL,
    OfferVersion int NOT NULL,
    PaymentTenantId uniqueidentifier NOT NULL,
    MerchantConfigId uniqueidentifier NOT NULL,
    PaymentOperationId uniqueidentifier NOT NULL,
    PaymentOrderId uniqueidentifier NULL,
    ProviderTransactionId varchar(128) NULL,
    AmountMinor bigint NOT NULL,
    Currency varchar(3) NOT NULL,
    SeatLimit bigint NOT NULL,
    StorageLimit bigint NOT NULL,
    EffectiveFromUtc datetimeoffset(7) NOT NULL,
    EffectiveToUtc datetimeoffset(7) NOT NULL,
    Phase varchar(32) NOT NULL,
    LastRefundId uniqueidentifier NULL,
    RefundAmountMinor bigint NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    UpdatedAtUtc datetimeoffset(7) NOT NULL,
    Version int NOT NULL,
    Subject nvarchar(128) NOT NULL,
    CONSTRAINT PK_fn_tenancy_subscription_invoice PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND name=N'UX_fn_tenancy_subscription_invoice_PaymentOperationId')
    CREATE UNIQUE INDEX UX_fn_tenancy_subscription_invoice_PaymentOperationId ON dbo.fn_tenancy_subscription_invoice(PaymentOperationId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND name=N'IX_fn_tenancy_subscription_invoice_TenantId_CreatedAtUtc')
    CREATE INDEX IX_fn_tenancy_subscription_invoice_TenantId_CreatedAtUtc ON dbo.fn_tenancy_subscription_invoice(TenantId,CreatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'订阅不可变报价及资金事实接收状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'复用订阅逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'权益受益租户',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'PackageId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结套餐标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'PackageId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'PackageVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结套餐元数据版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'PackageVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'OfferVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结报价版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'OfferVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'PaymentTenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'拥有收款商户的租户',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'PaymentTenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'MerchantConfigId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结收款商户',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'MerchantConfigId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'PaymentOperationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原始支付创建幂等身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'PaymentOperationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'PaymentOrderId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已验证的实际支付订单',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'PaymentOrderId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'ProviderTransactionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已验证的渠道交易标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'ProviderTransactionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'AmountMinor','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结最小单位金额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'AmountMinor';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'Currency','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结规范币种',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'Currency';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'SeatLimit','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结席位限额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'SeatLimit';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'StorageLimit','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结存储限额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'StorageLimit';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'EffectiveFromUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'权益生效时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'EffectiveFromUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'EffectiveToUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'权益到期时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'EffectiveToUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'Phase','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'待付款已履约退款或核对阶段',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'Phase';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'LastRefundId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近已接收的成功退款身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'LastRefundId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'RefundAmountMinor','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近已验证退款金额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'RefundAmountMinor';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近更新时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'接收状态乐观并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'RequestId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'调用方固定创建身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'RequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结购买输入摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'RequestHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'Subject','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结商品名称',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'Subject';
IF OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_invoice_entitlement (
    Id uniqueidentifier NOT NULL,
    InvoiceId uniqueidentifier NOT NULL,
    EntitlementId uniqueidentifier NOT NULL,
    EntitlementCode varchar(128) NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_tenancy_invoice_entitlement PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement') AND name=N'UX_fn_tenancy_invoice_entitlement_InvoiceId_EntitlementId')
    CREATE UNIQUE INDEX UX_fn_tenancy_invoice_entitlement_InvoiceId_EntitlementId ON dbo.fn_tenancy_invoice_entitlement(InvoiceId,EntitlementId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'报价时冻结的功能权益快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_invoice_entitlement';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'快照逻辑 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_invoice_entitlement',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement'),N'InvoiceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原始订阅报价',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_invoice_entitlement',@level2type=N'COLUMN',@level2name=N'InvoiceId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement'),N'EntitlementId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结权益目录标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_invoice_entitlement',@level2type=N'COLUMN',@level2name=N'EntitlementId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement'),N'EntitlementCode','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结稳定功能码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_invoice_entitlement',@level2type=N'COLUMN',@level2name=N'EntitlementCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_invoice_entitlement'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_invoice_entitlement',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF COL_LENGTH(N'dbo.fn_tenancy_tenant_entitlement_binding',N'SourceSubscriptionId') IS NULL ALTER TABLE dbo.fn_tenancy_tenant_entitlement_binding ADD SourceSubscriptionId uniqueidentifier NULL;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_tenant_entitlement_binding') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_entitlement_binding'),N'SourceSubscriptionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'自动授权的来源订阅；空值为独立人工授权',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_tenant_entitlement_binding',@level2type=N'COLUMN',@level2name=N'SourceSubscriptionId';
IF COL_LENGTH(N'dbo.fn_tenancy_quota_metric',N'SourceSubscriptionId') IS NULL ALTER TABLE dbo.fn_tenancy_quota_metric ADD SourceSubscriptionId uniqueidentifier NULL;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_quota_metric') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'),N'SourceSubscriptionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'提供本限额的来源订阅；空值为独立人工配置',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_quota_metric',@level2type=N'COLUMN',@level2name=N'SourceSubscriptionId';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND name=N'UX_fn_tenancy_subscription_invoice_TenantId_RequestId')
    CREATE UNIQUE INDEX UX_fn_tenancy_subscription_invoice_TenantId_RequestId ON dbo.fn_tenancy_subscription_invoice(TenantId,RequestId);
