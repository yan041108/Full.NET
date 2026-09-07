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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证OAuth 提供程序表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'Authority', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'颁发机构', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'Authority';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'ClientId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'ClientId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'ClientSecretProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的客户端密钥', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'ClientSecretProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'DisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'DisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'RedirectPath', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重定向路径', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'RedirectPath';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'Scopes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'授权范围', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'Scopes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_provider')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_provider'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_provider', @level2type=N'COLUMN', @level2name=N'Version';

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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证OAuth 用户绑定表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'DisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'DisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'Email', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'电子邮箱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'Email';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'EmailVerified', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'邮箱是否已验证', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'EmailVerified';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'LastUsedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后使用时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'LastUsedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'LinkedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Linked At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'LinkedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'Subject', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'主题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'Subject';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_user_link')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_user_link'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_user_link', @level2type=N'COLUMN', @level2name=N'Version';

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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证OAuth 授权状态表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'CodeVerifier', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'PKCE 校验码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'CodeVerifier';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'Mode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模式', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'Mode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'Nonce', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'随机数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'Nonce';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'ReturnUrl', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'返回地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'ReturnUrl';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oauth_authorization_state'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oauth_authorization_state', @level2type=N'COLUMN', @level2name=N'UserId';

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
