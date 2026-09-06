-- 151：OAuth/OIDC 提供程序、用户绑定与授权状态表。

IF OBJECT_ID(N'dbo.fn_identity_oauth_provider', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oauth_provider
    (
        Id uniqueidentifier NOT NULL,
        ProviderKey nvarchar(64) NOT NULL,
        DisplayName nvarchar(128) NOT NULL,
        Authority nvarchar(512) NOT NULL,
        ClientId nvarchar(256) NOT NULL,
        ClientSecretProtected nvarchar(max) NOT NULL,
        Scopes nvarchar(512) NOT NULL
            CONSTRAINT DF_fn_identity_oauth_provider_Scopes DEFAULT (N'openid profile email'),
        RedirectPath nvarchar(256) NOT NULL
            CONSTRAINT DF_fn_identity_oauth_provider_RedirectPath DEFAULT (N'/api/v1/identity/oauth/callback'),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_identity_oauth_provider_IsEnabled DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_oauth_provider_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_oauth_provider PRIMARY KEY CLUSTERED (Id)
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OAuth/OIDC 身份提供程序配置表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
      AND indexObject.name = N'UX_fn_identity_oauth_provider_ProviderKey'
)
    CREATE UNIQUE INDEX UX_fn_identity_oauth_provider_ProviderKey
        ON dbo.fn_identity_oauth_provider(ProviderKey);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
      AND indexObject.name = N'IX_fn_identity_oauth_provider_IsEnabled_DisplayName'
)
    CREATE INDEX IX_fn_identity_oauth_provider_IsEnabled_DisplayName
        ON dbo.fn_identity_oauth_provider(IsEnabled, DisplayName, Id);

IF OBJECT_ID(N'dbo.fn_identity_oauth_user_link', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oauth_user_link
    (
        Id uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        ProviderKey nvarchar(64) NOT NULL,
        Subject nvarchar(256) NOT NULL,
        Email nvarchar(256) NULL,
        EmailVerified bit NOT NULL
            CONSTRAINT DF_fn_identity_oauth_user_link_EmailVerified DEFAULT (0),
        DisplayName nvarchar(256) NULL,
        LinkedAtUtc datetimeoffset(7) NOT NULL,
        LastUsedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_oauth_user_link_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_oauth_user_link PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_identity_oauth_user_link_User
            FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user (Id)
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OAuth/OIDC 外部身份与本地用户绑定表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
      AND indexObject.name = N'UX_fn_identity_oauth_user_link_ProviderKey_Subject'
)
    CREATE UNIQUE INDEX UX_fn_identity_oauth_user_link_ProviderKey_Subject
        ON dbo.fn_identity_oauth_user_link(ProviderKey, Subject);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
      AND indexObject.name = N'UX_fn_identity_oauth_user_link_UserId_ProviderKey'
)
    CREATE UNIQUE INDEX UX_fn_identity_oauth_user_link_UserId_ProviderKey
        ON dbo.fn_identity_oauth_user_link(UserId, ProviderKey);

IF OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oauth_authorization_state
    (
        Id uniqueidentifier NOT NULL,
        ProviderKey nvarchar(64) NOT NULL,
        CodeVerifier nvarchar(128) NOT NULL,
        Nonce nvarchar(128) NOT NULL,
        Mode nvarchar(16) NOT NULL,
        UserId uniqueidentifier NULL,
        ReturnUrl nvarchar(2048) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oauth_authorization_state PRIMARY KEY NONCLUSTERED (Id)
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OAuth/OIDC 授权状态临时表（多实例安全）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
      AND indexObject.name = N'IX_fn_identity_oauth_authorization_state_ExpiresAtUtc'
)
    CREATE CLUSTERED INDEX IX_fn_identity_oauth_authorization_state_ExpiresAtUtc
        ON dbo.fn_identity_oauth_authorization_state(ExpiresAtUtc, Id);
