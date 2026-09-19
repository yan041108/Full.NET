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
-- fn_identity_oidc_application 的 SQL Server 对象注释（幂等补齐）
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证oidc application表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'ApplicationType', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Application Type', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'ApplicationType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'ClientId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'ClientId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'ClientSecret', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Client Secret', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'ClientSecret';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'ClientType', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Client Type', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'ClientType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'ConsentType', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Consent Type', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'ConsentType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'DisplayName', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'DisplayName';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'DisplayNamesJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Display Names(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'DisplayNamesJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'JsonWebKeySetJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Json Web Key Set(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'JsonWebKeySetJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'PermissionsJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限集合(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'PermissionsJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'PostLogoutRedirectUrisJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Post Logout Redirect Uris(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'PostLogoutRedirectUrisJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'PropertiesJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Properties(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'PropertiesJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'RedirectUrisJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Redirect Uris(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'RedirectUrisJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'RequirementsJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Requirements(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'RequirementsJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'SettingsJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Settings(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'SettingsJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_application') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_application'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_application', @level2type=N'COLUMN', @level2name=N'Version';

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
-- fn_identity_oidc_authorization 的 SQL Server 对象注释（幂等补齐）
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证oidc authorization表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'ApplicationId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Application标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'ApplicationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'CreationDateUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Creation Date(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'CreationDateUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'PropertiesJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Properties(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'PropertiesJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'ScopesJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Scopes(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'ScopesJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'Status', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'Status';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'Subject', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'主题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'Subject';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'Type', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'Type';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_authorization') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_authorization'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_authorization', @level2type=N'COLUMN', @level2name=N'Version';

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
-- fn_identity_oidc_scope 的 SQL Server 对象注释（幂等补齐）
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证oidc scope表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'Description', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'Description';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'DescriptionsJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Descriptions(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'DescriptionsJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'DisplayName', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'DisplayName';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'DisplayNamesJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Display Names(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'DisplayNamesJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'Name', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'Name';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'PropertiesJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Properties(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'PropertiesJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'ResourcesJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Resources(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'ResourcesJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_scope') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_scope'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_scope', @level2type=N'COLUMN', @level2name=N'Version';

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
-- fn_identity_oidc_token 的 SQL Server 对象注释（幂等补齐）
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证oidc token表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'ApplicationId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Application标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'ApplicationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'AuthorizationId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Authorization标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'AuthorizationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'CreationDateUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Creation Date(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'CreationDateUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'ExpirationDateUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Expiration Date(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'ExpirationDateUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'Payload', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息正文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'Payload';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'PropertiesJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Properties(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'PropertiesJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'RedemptionDateUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Redemption Date(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'RedemptionDateUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'ReferenceId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Reference标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'ReferenceId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'Status', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'Status';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'Subject', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'主题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'Subject';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'Type', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'Type';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_oidc_token') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_token'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_token', @level2type=N'COLUMN', @level2name=N'Version';

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