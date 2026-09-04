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
    CREATE CLUSTERED INDEX CIX_fn_notifications_endpoint_challenge_CreatedAtUtc
        ON dbo.fn_notifications_recipient_endpoint_challenge(CreatedAtUtc);
    CREATE NONCLUSTERED INDEX IX_fn_notifications_endpoint_challenge_Endpoint_Active
        ON dbo.fn_notifications_recipient_endpoint_challenge(RecipientEndpointId, ConsumedAtUtc, ExpiresAtUtc);
END;
