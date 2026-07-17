IF OBJECT_ID(N'dbo.fn_identity_user', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_user
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(64) NOT NULL,
        Username nvarchar(128) NOT NULL,
        NormalizedUsername nvarchar(128) NOT NULL,
        DisplayName nvarchar(128) NOT NULL,
        PasswordHash nvarchar(1024) NOT NULL,
        IsActive bit NOT NULL,
        FailedLoginCount int NOT NULL CONSTRAINT DF_fn_identity_user_FailedLoginCount DEFAULT (0),
        LockoutEndUtc datetimeoffset(7) NULL,
        SecurityStamp varchar(64) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_user_Version DEFAULT (1)
    );
    CREATE UNIQUE INDEX UX_fn_identity_user_Scope_Username
        ON dbo.fn_identity_user(ScopeKey, NormalizedUsername);
END;

IF OBJECT_ID(N'dbo.fn_identity_refresh_session', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_refresh_session
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        FamilyId uniqueidentifier NOT NULL,
        ClientId varchar(64) NOT NULL,
        TokenHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        ConsumedAtUtc datetimeoffset(7) NULL,
        RevokedAtUtc datetimeoffset(7) NULL,
        ReplacedById uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_refresh_session_Version DEFAULT (1),
        CONSTRAINT FK_fn_identity_refresh_session_User
            FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id)
    );
    CREATE UNIQUE INDEX UX_fn_identity_refresh_session_TokenHash
        ON dbo.fn_identity_refresh_session(TokenHash);
    CREATE INDEX IX_fn_identity_refresh_session_Family
        ON dbo.fn_identity_refresh_session(FamilyId, RevokedAtUtc, ExpiresAtUtc);
    CREATE INDEX IX_fn_identity_refresh_session_User
        ON dbo.fn_identity_refresh_session(UserId, RevokedAtUtc, ExpiresAtUtc);
END;

IF OBJECT_ID(N'dbo.fn_identity_auth_audit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_auth_audit
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        UserId uniqueidentifier NULL,
        SessionId uniqueidentifier NULL,
        UsernameFingerprint varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        EventType varchar(64) NOT NULL,
        ResultCode varchar(128) NOT NULL,
        Succeeded bit NOT NULL,
        IpAddress varchar(64) NULL,
        UserAgent nvarchar(512) NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT FK_fn_identity_auth_audit_User
            FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id)
    );
    CREATE INDEX IX_fn_identity_auth_audit_OccurredAt
        ON dbo.fn_identity_auth_audit(OccurredAtUtc);
    CREATE INDEX IX_fn_identity_auth_audit_User
        ON dbo.fn_identity_auth_audit(UserId, OccurredAtUtc);
END;
