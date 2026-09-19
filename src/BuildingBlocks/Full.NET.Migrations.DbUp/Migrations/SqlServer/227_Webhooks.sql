-- 227: webhook subscriptions and delivery attempts.
IF OBJECT_ID(N'dbo.fn_webhooks_subscription', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_webhooks_subscription (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        EventType nvarchar(128) NOT NULL,
        TargetUrl nvarchar(2048) NOT NULL,
        SigningSecretHash nvarchar(1024) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_fn_webhooks_subscription_IsActive DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_webhooks_subscription_Version DEFAULT (1),
        CONSTRAINT PK_fn_webhooks_subscription PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'webhookssubscription表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'EventType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'EventType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'SigningSecretHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Signing Secret Hash', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'SigningSecretHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'TargetUrl', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Target Url', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'TargetUrl';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_subscription', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_webhooks_delivery', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_webhooks_delivery (
        Id uniqueidentifier NOT NULL,
        SubscriptionId uniqueidentifier NOT NULL,
        EventId uniqueidentifier NOT NULL,
        PayloadBody nvarchar(max) NOT NULL,
        PayloadDigest nvarchar(128) NOT NULL,
        Status nvarchar(32) NOT NULL,
        AttemptCount int NOT NULL CONSTRAINT DF_fn_webhooks_delivery_AttemptCount DEFAULT (0),
        NextAttemptAtUtc datetimeoffset(7) NULL,
        LastError nvarchar(512) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_webhooks_delivery_Version DEFAULT (1),
        CONSTRAINT PK_fn_webhooks_delivery PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'webhooks渠道投递表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'AttemptCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'AttemptCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'EventId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Event标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'EventId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'LastError', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后错误', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'LastError';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'NextAttemptAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下次重试时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'NextAttemptAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'PayloadBody', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Payload Body', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'PayloadBody';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'PayloadDigest', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'载荷摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'PayloadDigest';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'SubscriptionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Subscription标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'SubscriptionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_delivery'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_delivery', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_webhooks_delivery_EventSubscription' AND object_id = OBJECT_ID(N'dbo.fn_webhooks_delivery'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_webhooks_delivery_EventSubscription
        ON dbo.fn_webhooks_delivery (SubscriptionId, EventId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_webhooks_delivery_Pending' AND object_id = OBJECT_ID(N'dbo.fn_webhooks_delivery'))
    CREATE NONCLUSTERED INDEX IX_fn_webhooks_delivery_Pending
        ON dbo.fn_webhooks_delivery (Status, NextAttemptAtUtc, CreatedAtUtc);
