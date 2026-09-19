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
-- fn_identity_oidc_center_session 的 SQL Server 对象注释（幂等补齐）
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证oidc center session表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_center_session'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_center_session'), N'ExpiresAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_center_session'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_center_session'), N'RevokedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'撤销时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session', @level2type=N'COLUMN', @level2name=N'RevokedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_center_session'), N'SecurityStamp', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'安全戳', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session', @level2type=N'COLUMN', @level2name=N'SecurityStamp';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_center_session'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_center_session'), N'UserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session', @level2type=N'COLUMN', @level2name=N'UserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_center_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_center_session'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_center_session', @level2type=N'COLUMN', @level2name=N'Version';

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
-- fn_identity_oidc_application_session 的 SQL Server 对象注释（幂等补齐）
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证oidc application session表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'ActiveTenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前活动租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'ActiveTenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'ActorScope', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Actor Scope', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'ActorScope';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'CenterSessionId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Center Session标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'CenterSessionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'ClientId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'ClientId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'EffectiveScope', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Effective Scope', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'EffectiveScope';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'ExpiresAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'OidcApplicationId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Oidc Application标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'OidcApplicationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'RevokedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'撤销时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'RevokedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'UserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'UserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application_session'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application_session', @level2type=N'COLUMN', @level2name=N'Version';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_application_session_CenterSessionId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_application_session_CenterSessionId
        ON dbo.fn_identity_oidc_application_session (CenterSessionId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_application_session_UserId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_application_session_UserId
        ON dbo.fn_identity_oidc_application_session (UserId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_oidc_application_session_UserId_ClientId' AND object_id = OBJECT_ID(N'dbo.fn_identity_oidc_application_session'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_oidc_application_session_UserId_ClientId
        ON dbo.fn_identity_oidc_application_session (UserId, ClientId);