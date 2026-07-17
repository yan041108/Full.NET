IF OBJECT_ID(N'dbo.fn_identity_role', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_role
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(64) NOT NULL,
        Code varchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        IsSystem bit NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_role_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_role PRIMARY KEY (Id)
    );
    CREATE UNIQUE INDEX UX_fn_identity_role_Scope_Code
        ON dbo.fn_identity_role(ScopeKey, Code);
    CREATE INDEX IX_fn_identity_role_Tenant
        ON dbo.fn_identity_role(TenantId, IsActive);
END;

IF OBJECT_ID(N'dbo.fn_identity_user_role', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_user_role
    (
        UserId uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_identity_user_role PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_fn_identity_user_role_User
            FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id),
        CONSTRAINT FK_fn_identity_user_role_Role
            FOREIGN KEY (RoleId) REFERENCES dbo.fn_identity_role(Id)
    );
END;

IF OBJECT_ID(N'dbo.fn_identity_role_permission', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_role_permission
    (
        RoleId uniqueidentifier NOT NULL,
        PermissionCode varchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CONSTRAINT PK_fn_identity_role_permission PRIMARY KEY (RoleId, PermissionCode),
        CONSTRAINT FK_fn_identity_role_permission_Role
            FOREIGN KEY (RoleId) REFERENCES dbo.fn_identity_role(Id)
    );
END;

IF COL_LENGTH(N'dbo.fn_identity_refresh_session', N'ActiveTenantId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_refresh_session
        ADD ActiveTenantId uniqueidentifier NULL;
END;

IF COL_LENGTH(N'dbo.fn_identity_auth_audit', N'ContextTenantId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_auth_audit
        ADD ContextTenantId uniqueidentifier NULL;
END;
