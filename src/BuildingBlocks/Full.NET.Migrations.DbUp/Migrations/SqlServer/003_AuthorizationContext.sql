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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证角色表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'Code';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'IsActive';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否系统内置', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'IsSystem';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'Name';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'Version';
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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证用户角色表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_role';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'角色标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_role', @level2type=N'COLUMN', @level2name=N'RoleId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_role', @level2type=N'COLUMN', @level2name=N'UserId';
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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证角色权限表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_permission';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_permission', @level2type=N'COLUMN', @level2name=N'PermissionCode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'角色标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_permission', @level2type=N'COLUMN', @level2name=N'RoleId';
END;

IF COL_LENGTH(N'dbo.fn_identity_refresh_session', N'ActiveTenantId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_refresh_session
        ADD ActiveTenantId uniqueidentifier NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前活动租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ActiveTenantId';
END;

IF COL_LENGTH(N'dbo.fn_identity_auth_audit', N'ContextTenantId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_auth_audit
        ADD ContextTenantId uniqueidentifier NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'上下文租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'ContextTenantId';
END;
