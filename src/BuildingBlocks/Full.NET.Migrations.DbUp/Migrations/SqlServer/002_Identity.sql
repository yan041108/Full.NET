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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证用户表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'DisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'DisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'FailedLoginCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'登录失败次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'FailedLoginCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'LockoutEndUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'锁定结束时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'LockoutEndUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'NormalizedUsername', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规范化用户名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'NormalizedUsername';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'PasswordHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密码哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'PasswordHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'SecurityStamp', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'安全戳', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'SecurityStamp';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'Username', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'Username';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户刷新令牌会话', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'ClientId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ClientId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'ConsumedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ConsumedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'FamilyId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'会话族标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'FamilyId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'ReplacedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'替换会话标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'ReplacedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'RevokedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'撤销时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'RevokedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'TokenHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'令牌哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'TokenHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_refresh_session', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_refresh_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_refresh_session'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证审计事件', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'EventType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'EventType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'IpAddress', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'IP 地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'IpAddress';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'OccurredAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'ResultCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'ResultCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'SessionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'会话标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'SessionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'Succeeded', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否成功', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'Succeeded';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'UserAgent', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户代理', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'UserAgent';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'UsernameFingerprint', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户名指纹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'UsernameFingerprint';
    CREATE INDEX IX_fn_identity_auth_audit_OccurredAt
        ON dbo.fn_identity_auth_audit(OccurredAtUtc);
    CREATE INDEX IX_fn_identity_auth_audit_User
        ON dbo.fn_identity_auth_audit(UserId, OccurredAtUtc);
END;
