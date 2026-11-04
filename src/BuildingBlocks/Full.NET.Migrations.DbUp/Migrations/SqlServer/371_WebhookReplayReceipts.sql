-- 原事件和载荷不变；回执与重新排队同事务提交，不通过 Outbox 替代本地审计。
-- 表、索引及说明逐项探测；已执行未记账可重入，回退须保留已提交的回执。
IF OBJECT_ID(N'dbo.fn_webhooks_delivery_replay',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_webhooks_delivery_replay (
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
        QueuedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_webhooks_delivery_replay PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_webhooks_delivery_replay_Version CHECK (PreviousVersion>=1 AND Version=PreviousVersion+1 AND AttemptCount>=0)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND name=N'CX_fn_webhooks_delivery_replay_QueuedId')
    CREATE CLUSTERED INDEX CX_fn_webhooks_delivery_replay_QueuedId ON dbo.fn_webhooks_delivery_replay(QueuedAtUtc,Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND name=N'UX_fn_webhooks_delivery_replay_TenantOperation')
    CREATE UNIQUE INDEX UX_fn_webhooks_delivery_replay_TenantOperation ON dbo.fn_webhooks_delivery_replay(TenantId,OperationId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'手动重放的不可变排队回执及同事务领域审计',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端UUID v7回执主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信企业标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'OperationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'客户端固定操作标识，重试不得替换',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'OperationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际操作者账号标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'ActorUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'ActorSessionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'提交时的原交互会话标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'ActorSessionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'SubscriptionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原订阅标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'SubscriptionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'DeliveryId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原投递标识，不新建投递',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'DeliveryId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'EventId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'保持不变的原业务事件身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'EventId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'PreviousVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'排队前终态失败投递修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'PreviousVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'排队成功后的投递修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'AttemptCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'排队时原已完成次数，不重置',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'AttemptCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery_replay'),N'QueuedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'排队事务的UTC时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'QueuedAtUtc';
