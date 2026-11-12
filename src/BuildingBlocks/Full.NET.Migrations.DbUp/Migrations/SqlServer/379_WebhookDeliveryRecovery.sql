-- 待发送状态与恢复回执同事务提交。重复运行补齐说明且保留原审计。
IF OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_webhooks_delivery_recovery (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        ActorSessionId uniqueidentifier NOT NULL,
        SubscriptionId uniqueidentifier NOT NULL,
        DeliveryId uniqueidentifier NOT NULL,
        EventId uniqueidentifier NOT NULL,
        PreviousVersion int NOT NULL,
        Version int NOT NULL,
        AttemptCount int NOT NULL,
        PayloadSha256 char(64) NOT NULL,
        ReceiverReceiptSha256 char(64) NOT NULL,
        ConfirmedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_webhooks_delivery_recovery PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_webhooks_delivery_recovery_TenantOperation UNIQUE (TenantId,OperationId),
        CONSTRAINT CK_fn_webhooks_delivery_recovery_Version CHECK (PreviousVersion>=1 AND Version=PreviousVersion+1 AND AttemptCount>=0)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'接收方已提交投递的恢复确认和同事务审计',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端 UUID v7 回执主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'OperationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定操作标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'OperationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'提交确认的操作者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'ActorUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'ActorSessionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'提交确认时的会话',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'ActorSessionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'SubscriptionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原订阅标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'SubscriptionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'DeliveryId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原投递标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'DeliveryId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'EventId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原事件标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'EventId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'PreviousVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'对账前投递修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'PreviousVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'确认后投递修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'AttemptCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发送方原实际尝试次数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'AttemptCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'PayloadSha256','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原 HTTP 请求体 SHA-256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'PayloadSha256';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'ReceiverReceiptSha256','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'权威接收方证据原始字节 SHA-256',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'ReceiverReceiptSha256';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_recovery'),N'ConfirmedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'确认提交时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_recovery',@level2type=N'COLUMN',@level2name=N'ConfirmedAtUtc';
