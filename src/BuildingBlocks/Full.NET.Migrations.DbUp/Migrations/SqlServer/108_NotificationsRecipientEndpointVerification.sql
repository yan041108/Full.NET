-- 108：收件端点邮件验证码挑战表；支持 pending → verified 的受控升级，不暴露验证码原文。
IF OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_recipient_endpoint_challenge
    (
        Id uniqueidentifier NOT NULL,
        RecipientEndpointId uniqueidentifier NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        UserId uniqueidentifier NOT NULL,
        CodeHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AttemptCount int NOT NULL CONSTRAINT DF_fn_notifications_endpoint_challenge_AttemptCount DEFAULT (0),
        MaxAttempts int NOT NULL CONSTRAINT DF_fn_notifications_endpoint_challenge_MaxAttempts DEFAULT (5),
        ExpiresAtUtc datetime2(6) NOT NULL,
        ConsumedAtUtc datetime2(6) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_recipient_endpoint_challenge PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_endpoint_challenge_Endpoint
            FOREIGN KEY (RecipientEndpointId) REFERENCES dbo.fn_notifications_recipient_endpoint(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知收件端点验证挑战表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'AttemptCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'AttemptCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'CodeHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'验证码摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'CodeHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'ConsumedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'ConsumedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'MaxAttempts', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最大校验尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'MaxAttempts';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'RecipientEndpointId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'收件端点标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'RecipientEndpointId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint_challenge'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint_challenge', @level2type=N'COLUMN', @level2name=N'UserId';
    CREATE CLUSTERED INDEX CIX_fn_notifications_endpoint_challenge_CreatedAtUtc
        ON dbo.fn_notifications_recipient_endpoint_challenge(CreatedAtUtc);
    CREATE NONCLUSTERED INDEX IX_fn_notifications_endpoint_challenge_Endpoint_Active
        ON dbo.fn_notifications_recipient_endpoint_challenge(RecipientEndpointId, ConsumedAtUtc, ExpiresAtUtc);
END;
