-- 216：Identity OIDC 协议状态表；expand-only，支持幂等重跑。
IF OBJECT_ID(N'dbo.fn_identity_oidc_application', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_application (
        Id uniqueidentifier NOT NULL,
        ClientId nvarchar(256) NOT NULL,
        ClientSecret nvarchar(max) NULL,
        ConsentType nvarchar(64) NULL,
        DisplayName nvarchar(256) NULL,
        DisplayNamesJson nvarchar(max) NULL,
        PermissionsJson nvarchar(max) NULL,
        PostLogoutRedirectUrisJson nvarchar(max) NULL,
        PropertiesJson nvarchar(max) NULL,
        RedirectUrisJson nvarchar(max) NULL,
        RequirementsJson nvarchar(max) NULL,
        ApplicationType nvarchar(64) NULL,
        JsonWebKeySetJson nvarchar(max) NULL,
        SettingsJson nvarchar(max) NULL,
        ClientType nvarchar(64) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_identity_oidc_application_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_application PRIMARY KEY NONCLUSTERED (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_identity_oidc_application_ClientId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_application'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_identity_oidc_application_ClientId
        ON dbo.fn_identity_oidc_application (ClientId);

IF OBJECT_ID(N'dbo.fn_identity_oidc_authorization', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_authorization (
        Id uniqueidentifier NOT NULL,
        ApplicationId uniqueidentifier NULL,
        CreationDateUtc datetimeoffset(7) NULL,
        PropertiesJson nvarchar(max) NULL,
        ScopesJson nvarchar(max) NULL,
        Status nvarchar(64) NULL,
        Subject nvarchar(512) NULL,
        Type nvarchar(128) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_identity_oidc_authorization_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_authorization PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_identity_oidc_authorization_Application
            FOREIGN KEY (ApplicationId) REFERENCES dbo.fn_identity_oidc_application (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_authorization_ApplicationId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_authorization_ApplicationId
        ON dbo.fn_identity_oidc_authorization (ApplicationId);

IF OBJECT_ID(N'dbo.fn_identity_oidc_scope', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_scope (
        Id uniqueidentifier NOT NULL,
        Name nvarchar(256) NOT NULL,
        Description nvarchar(512) NULL,
        DescriptionsJson nvarchar(max) NULL,
        DisplayName nvarchar(256) NULL,
        DisplayNamesJson nvarchar(max) NULL,
        PropertiesJson nvarchar(max) NULL,
        ResourcesJson nvarchar(max) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_identity_oidc_scope_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_scope PRIMARY KEY NONCLUSTERED (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_identity_oidc_scope_Name' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_identity_oidc_scope_Name
        ON dbo.fn_identity_oidc_scope (Name);

IF OBJECT_ID(N'dbo.fn_identity_oidc_token', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_token (
        Id uniqueidentifier NOT NULL,
        ApplicationId uniqueidentifier NULL,
        AuthorizationId uniqueidentifier NULL,
        CreationDateUtc datetimeoffset(7) NULL,
        ExpirationDateUtc datetimeoffset(7) NULL,
        Payload nvarchar(max) NULL,
        PropertiesJson nvarchar(max) NULL,
        RedemptionDateUtc datetimeoffset(7) NULL,
        ReferenceId nvarchar(256) NULL,
        Status nvarchar(64) NULL,
        Subject nvarchar(512) NULL,
        Type nvarchar(256) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_identity_oidc_token_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_token PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_identity_oidc_token_Application
            FOREIGN KEY (ApplicationId) REFERENCES dbo.fn_identity_oidc_application (Id),
        CONSTRAINT FK_fn_identity_oidc_token_Authorization
            FOREIGN KEY (AuthorizationId) REFERENCES dbo.fn_identity_oidc_authorization (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_token_ApplicationId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_token'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_token_ApplicationId
        ON dbo.fn_identity_oidc_token (ApplicationId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_token_AuthorizationId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_token'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_token_AuthorizationId
        ON dbo.fn_identity_oidc_token (AuthorizationId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_identity_oidc_token_ReferenceId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_token'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_identity_oidc_token_ReferenceId
        ON dbo.fn_identity_oidc_token (ReferenceId)
        WHERE ReferenceId IS NOT NULL;