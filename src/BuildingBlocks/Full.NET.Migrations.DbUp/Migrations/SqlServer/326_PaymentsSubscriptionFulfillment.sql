-- 冻结业务报价与资金身份；不对其他模块表建立外键。
IF OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_payment_subscription_fulfillment (
    Id uniqueidentifier NOT NULL,
    SubscriptionId uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    BeneficiaryTenantId uniqueidentifier NOT NULL,
    MerchantConfigId uniqueidentifier NOT NULL,
    AmountMinor bigint NOT NULL,
    Currency varchar(3) NOT NULL,
    EffectiveFromUtc datetimeoffset(7) NOT NULL,
    EffectiveToUtc datetimeoffset(7) NOT NULL,
    PaymentOrderId uniqueidentifier NULL,
    ProviderTransactionId varchar(128) NULL,
    PaidAtUtc datetimeoffset(7) NULL,
    LastRefundId uniqueidentifier NULL,
    RefundAmountMinor bigint NOT NULL,
    Phase varchar(32) NOT NULL,
    ErrorCode varchar(128) NULL,
    DueAtUtc datetimeoffset(7) NOT NULL,
    LeaseId uniqueidentifier NULL,
    LeaseUntilUtc datetimeoffset(7) NULL,
    Attempt int NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    UpdatedAtUtc datetimeoffset(7) NOT NULL,
    Version int NOT NULL,
    CONSTRAINT PK_fn_payment_subscription_fulfillment PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND name=N'IX_fn_payment_subscription_fulfillment_Phase_DueAtUtc')
    CREATE INDEX IX_fn_payment_subscription_fulfillment_Phase_DueAtUtc ON dbo.fn_payment_subscription_fulfillment(Phase,DueAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND name=N'IX_fn_payment_subscription_fulfillment_SubscriptionId')
    CREATE INDEX IX_fn_payment_subscription_fulfillment_SubscriptionId ON dbo.fn_payment_subscription_fulfillment(SubscriptionId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Payments 自有订阅履约恢复与资金绑定',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'支付创建幂等身份 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'SubscriptionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Tenancy 原始订阅',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'SubscriptionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'支付收款租户',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'BeneficiaryTenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'权益受益租户',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'BeneficiaryTenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'MerchantConfigId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结收款商户',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'MerchantConfigId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'AmountMinor','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结最小单位金额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'AmountMinor';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'Currency','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结规范币种',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'Currency';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'EffectiveFromUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'权益最早生效 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'EffectiveFromUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'EffectiveToUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'权益最晚生效 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'EffectiveToUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'PaymentOrderId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际资金订单',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'PaymentOrderId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'ProviderTransactionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已验证渠道交易标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'ProviderTransactionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'PaidAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已验证支付时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'PaidAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'LastRefundId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近成功退款身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'LastRefundId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'RefundAmountMinor','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近已验证退款金额',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'RefundAmountMinor';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'Phase','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建付款履约退款或核对阶段',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'Phase';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'ErrorCode','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近稳定失败码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'ErrorCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'下次恢复时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'LeaseId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前恢复租约身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'LeaseId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'LeaseUntilUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前租约到期 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'LeaseUntilUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'Attempt','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'连续恢复次数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'Attempt';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近更新时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_payment_subscription_fulfillment'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'工作项乐观并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_subscription_fulfillment',@level2type=N'COLUMN',@level2name=N'Version';
