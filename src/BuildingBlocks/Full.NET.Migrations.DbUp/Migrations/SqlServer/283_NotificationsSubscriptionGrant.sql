-- 一次交互最多一份本地许可；历史 accepted 不推导库存。
IF OBJECT_ID(N'dbo.fn_notifications_subscription_grant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_subscription_grant (
        Id uniqueidentifier NOT NULL,
        BindingId uniqueidentifier NOT NULL,
        TemplateId varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AuthorizationId uniqueidentifier NOT NULL,
        ReportedStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        StateKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DeliveryId uniqueidentifier NULL,
        LeaseGeneration bigint NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_notifications_subscription_grant PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_fn_notifications_subscription_grant_Authorization UNIQUE (BindingId, TemplateId, AuthorizationId)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'一次性本地订阅同意台账，客户端记录不是厂商库存证明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用生成的授权台账标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'BindingId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户与应用绑定标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'BindingId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'TemplateId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'微信订阅模板标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'TemplateId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'AuthorizationId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端订阅交互幂等标识，不代表厂商可信库存', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'AuthorizationId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'StateKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本地授权可用、预留、核销或待对账状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'StateKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'DeliveryId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留该授权的投递标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'DeliveryId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'LeaseGeneration', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留投递租约世代', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'LeaseGeneration';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本地授权事实登记时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND name = N'IX_fn_notifications_subscription_grant_CreatedAtUtc')
    CREATE CLUSTERED INDEX IX_fn_notifications_subscription_grant_CreatedAtUtc ON dbo.fn_notifications_subscription_grant (CreatedAtUtc, Id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND name = N'IX_fn_notifications_subscription_grant_DeliveryId')
    CREATE INDEX IX_fn_notifications_subscription_grant_DeliveryId ON dbo.fn_notifications_subscription_grant (DeliveryId, StateKey);

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_subscription_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_subscription_grant'), N'ReportedStatusKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首次客户端上报的不可变接受或拒绝状态，用于拒绝冲突重放', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_subscription_grant', @level2type=N'COLUMN', @level2name=N'ReportedStatusKey';
