-- 领取与开始记录同事务；首次完成后不再更新该尝试，不为历史行补造已经发出的请求。
-- 表、索引及说明逐步探测；创建完成但未记账时可重入，回退前须保留已有尝试证据。
IF OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_webhooks_delivery_attempt (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        SubscriptionId uniqueidentifier NOT NULL,
        DeliveryId uniqueidentifier NOT NULL,
        ClaimVersion int NOT NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        OutcomeKey varchar(16) NOT NULL,
        HttpStatus int NULL,
        ErrorCode varchar(128) NULL,
        AppliedToDelivery bit NULL,
        CONSTRAINT PK_fn_webhooks_delivery_attempt PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_webhooks_delivery_attempt_Outcome CHECK (
            (OutcomeKey='started' AND CompletedAtUtc IS NULL AND AppliedToDelivery IS NULL)
            OR (OutcomeKey IN ('delivered','failed') AND CompletedAtUtc IS NOT NULL AND AppliedToDelivery IS NOT NULL))
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND name=N'CX_fn_webhooks_delivery_attempt_StartedId')
    CREATE CLUSTERED INDEX CX_fn_webhooks_delivery_attempt_StartedId ON dbo.fn_webhooks_delivery_attempt(StartedAtUtc,Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND name=N'UX_fn_webhooks_delivery_attempt_DeliveryClaim')
    CREATE UNIQUE INDEX UX_fn_webhooks_delivery_attempt_DeliveryClaim ON dbo.fn_webhooks_delivery_attempt(DeliveryId,ClaimVersion);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Webhook逐次尝试的开始及首次完成事实，完成后不可变',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端UUID v7尝试标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信订阅所属企业',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'SubscriptionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原订阅标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'SubscriptionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'DeliveryId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原投递标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'DeliveryId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'ClaimVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'成功领取时的投递修订，作为本次执行隔离标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'ClaimVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'StartedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'领取提交的开始时间，不代表HTTP已发送',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'StartedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次确认结果的UTC时间，未确认时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'OutcomeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'started、delivered或failed的首次完成状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'OutcomeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'HttpStatus','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已知HTTP应答状态，非HTTP故障为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'HttpStatus';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'ErrorCode','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'安全失败分类，不存原始异常或正文',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'ErrorCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt'),N'AppliedToDelivery','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'该完成是否推进原租约版本的投递状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'AppliedToDelivery';
