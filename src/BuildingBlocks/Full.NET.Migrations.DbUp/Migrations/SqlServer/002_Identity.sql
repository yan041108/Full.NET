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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证用户表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'DisplayName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'登录失败次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'FailedLoginCount';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'IsActive';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'锁定结束时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'LockoutEndUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规范化用户名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'NormalizedUsername';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密码哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'PasswordHash';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'安全戳', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'SecurityStamp';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'Username';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'Version';
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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户刷新令牌会话', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ClientId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ConsumedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'会话族标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'FamilyId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'替换会话标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ReplacedById';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'撤销时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'RevokedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'令牌哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'TokenHash';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'UserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'Version';
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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证审计事件', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'EventType';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'IP 地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'IpAddress';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'ResultCode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'会话标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'SessionId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否成功', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'Succeeded';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户代理', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'UserAgent';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'UserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户名指纹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'UsernameFingerprint';
    CREATE INDEX IX_fn_identity_auth_audit_OccurredAt
        ON dbo.fn_identity_auth_audit(OccurredAtUtc);
    CREATE INDEX IX_fn_identity_auth_audit_User
        ON dbo.fn_identity_auth_audit(UserId, OccurredAtUtc);
END;
