-- 217：Identity OIDC 中心会话与应用会话表；expand-only，支持幂等重跑。
IF OBJECT_ID(N'dbo.fn_identity_oidc_center_session', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_center_session (
        Id uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        SecurityStamp nvarchar(64) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        RevokedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_identity_oidc_center_session_Version DEFAULT (1),
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_center_session PRIMARY KEY NONCLUSTERED (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_center_session_UserId_Active' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_center_session_UserId_Active
        ON dbo.fn_identity_oidc_center_session (UserId)
        WHERE RevokedAtUtc IS NULL;

IF OBJECT_ID(N'dbo.fn_identity_oidc_application_session', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_application_session (
        Id uniqueidentifier NOT NULL,
        CenterSessionId uniqueidentifier NOT NULL,
        OidcApplicationId uniqueidentifier NOT NULL,
        ClientId nvarchar(256) NOT NULL,
        UserId uniqueidentifier NOT NULL,
        ActorScope nvarchar(64) NOT NULL,
        EffectiveScope nvarchar(128) NOT NULL,
        ActiveTenantId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        RevokedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_identity_oidc_application_session_Version DEFAULT (1),
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_application_session PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_identity_oidc_application_session_Center
            FOREIGN KEY (CenterSessionId) REFERENCES dbo.fn_identity_oidc_center_session (Id),
        CONSTRAINT FK_fn_identity_oidc_application_session_Application
            FOREIGN KEY (OidcApplicationId) REFERENCES dbo.fn_identity_oidc_application (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_application_session_CenterSessionId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_application_session_CenterSessionId
        ON dbo.fn_identity_oidc_application_session (CenterSessionId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_application_session_UserId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_application_session_UserId
        ON dbo.fn_identity_oidc_application_session (UserId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_application_session_UserId_ClientId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_application_session_UserId_ClientId
        ON dbo.fn_identity_oidc_application_session (UserId, ClientId);