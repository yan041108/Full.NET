-- Outbox 消费生成本地幂等投递意图，失败与重启均保留同一 jti。
IF OBJECT_ID('dbo.fn_identity_oidc_logout_delivery', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_logout_delivery (
        Id uniqueidentifier NOT NULL,
        CenterSessionId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        ClientId nvarchar(256) NOT NULL,
        TargetUri nvarchar(2048) NOT NULL,
        StatusKey varchar(16) NOT NULL,
        AttemptCount int NOT NULL,
        NextAttemptAtUtc datetimeoffset(7) NOT NULL,
        LeaseId uniqueidentifier NULL,
        LeaseUntilUtc datetimeoffset(7) NULL,
        LastErrorCode varchar(64) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        DeliveredAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_identity_oidc_logout_delivery PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_fn_identity_oidc_logout_delivery_session_client UNIQUE (CenterSessionId, ClientId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND name = N'IX_fn_identity_oidc_logout_delivery_due')
    CREATE INDEX IX_fn_identity_oidc_logout_delivery_due ON dbo.fn_identity_oidc_logout_delivery(StatusKey, NextAttemptAtUtc, Id);

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OIDC 标准退出通知投递状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用端 UUID v7，亦为 logout_token 的稳定 jti', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'Id';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'CenterSessionId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应撤销的中心浏览器会话 sid', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'CenterSessionId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'UserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'会话所属用户 sub', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'UserId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'ClientId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接收方 client_id 和令牌 audience', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'ClientId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'TargetUri', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'撤销事务冻结的部署许可 HTTPS 接收端', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'TargetUri';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'StatusKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'pending delivered 或 failed', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'StatusKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'AttemptCount', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本轮已领取投递次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'AttemptCount';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'NextAttemptAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下次可重试时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'NextAttemptAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'LeaseId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Worker 本次投递租约标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'LeaseId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'LeaseUntilUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Worker 租约截止时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'LeaseUntilUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'LastErrorCode', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不含令牌或异常详情的稳定失败码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'LastErrorCode';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首次投递意图时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_logout_delivery'), N'DeliveredAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认送达时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_logout_delivery', @level2type=N'COLUMN', @level2name=N'DeliveredAtUtc';
