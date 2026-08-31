-- 104：建立 Notifications 平台内核表（模板版本、Intent/Recipient、投递/尝试/回执、Profile/Binding、偏好与领域审计）。
-- SQL Server 高写入表使用非聚集主键并配套时间聚集索引；发布版本表禁止更新。
IF OBJECT_ID(N'dbo.fn_notifications_template', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_template
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TemplateKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ChannelKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ContentCategoryKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DraftSubject nvarchar(256) NOT NULL,
        DraftBodyJson nvarchar(max) NOT NULL,
        DraftParameterSchemaJson nvarchar(max) NOT NULL,
        DraftRevision bigint NOT NULL,
        LatestPublishedVersionId uniqueidentifier NULL,
        CreatedById uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_notifications_template_Version DEFAULT (1),
        CONSTRAINT PK_fn_notifications_template PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_notifications_template_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_notifications_template_Category CHECK (ContentCategoryKey IN ('mandatory', 'transactional', 'informational', 'marketing')),
        CONSTRAINT CK_fn_notifications_template_Revision CHECK (DraftRevision > 0),
        CONSTRAINT CK_fn_notifications_template_Version CHECK (Version > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知模板表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'ChannelKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'ChannelKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'ContentCategoryKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容政策类别键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'ContentCategoryKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'DraftBodyJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿正文(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'DraftBodyJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'DraftParameterSchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿参数结构(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'DraftParameterSchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'DraftRevision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'DraftRevision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'DraftSubject', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿主题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'DraftSubject';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'LatestPublishedVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最新已发布版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'LatestPublishedVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'TemplateKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模板稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'TemplateKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_notifications_template_Scope_Key
        ON dbo.fn_notifications_template(TenantScopeKey, TemplateKey);
END;

IF OBJECT_ID(N'dbo.fn_notifications_template_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_template_version
    (
        Id uniqueidentifier NOT NULL,
        TemplateId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        SchemaVersion int NOT NULL,
        Subject nvarchar(256) NOT NULL,
        BodyJson nvarchar(max) NOT NULL,
        ParameterSchemaJson nvarchar(max) NOT NULL,
        ContentClassificationKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublishedById uniqueidentifier NOT NULL,
        PublishedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_template_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_template_version_Template FOREIGN KEY (TemplateId) REFERENCES dbo.fn_notifications_template(Id),
        CONSTRAINT CK_fn_notifications_template_version_Number CHECK (VersionNumber > 0),
        CONSTRAINT CK_fn_notifications_template_version_Schema CHECK (SchemaVersion > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知模板版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'BodyJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Body(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'BodyJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'ContentClassificationKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容安全分级键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'ContentClassificationKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'ParameterSchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'参数结构(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'ParameterSchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'PublishedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'PublishedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'SchemaVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema 版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'Subject', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'主题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'Subject';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'TemplateId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模板标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'TemplateId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_template_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_template_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
    CREATE UNIQUE INDEX UX_fn_notifications_template_version_Number
        ON dbo.fn_notifications_template_version(TemplateId, VersionNumber);
    CREATE UNIQUE INDEX UX_fn_notifications_template_version_Hash
        ON dbo.fn_notifications_template_version(TemplateId, ContentHash);
END;

IF OBJECT_ID(N'dbo.fn_notifications_provider_profile', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_provider_profile
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProfileKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProviderTypeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        NonSecretConfigJson nvarchar(max) NOT NULL,
        SecretReference nvarchar(256) NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_fn_notifications_provider_profile_Enabled DEFAULT (0),
        DraftRevision bigint NOT NULL,
        LatestPublishedVersionId uniqueidentifier NULL,
        CreatedById uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_notifications_provider_profile_Version DEFAULT (1),
        CONSTRAINT PK_fn_notifications_provider_profile PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_notifications_provider_profile_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_notifications_provider_profile_Revision CHECK (DraftRevision > 0),
        CONSTRAINT CK_fn_notifications_provider_profile_Version CHECK (Version > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知渠道配置表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'DraftRevision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'DraftRevision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'LatestPublishedVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最新已发布版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'LatestPublishedVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'NonSecretConfigJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'非密钥配置(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'NonSecretConfigJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'ProfileKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道配置稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'ProfileKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'ProviderTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'ProviderTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'SecretReference', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密钥引用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'SecretReference';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_notifications_provider_profile_Scope_Key
        ON dbo.fn_notifications_provider_profile(TenantScopeKey, ProfileKey);
END;

IF OBJECT_ID(N'dbo.fn_notifications_provider_profile_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_provider_profile_version
    (
        Id uniqueidentifier NOT NULL,
        ProfileId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        ProviderTypeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AdapterVersion varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        NonSecretConfigJson nvarchar(max) NOT NULL,
        SecretReference nvarchar(256) NULL,
        ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublishedById uniqueidentifier NOT NULL,
        PublishedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_provider_profile_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_profile_version_Profile FOREIGN KEY (ProfileId) REFERENCES dbo.fn_notifications_provider_profile(Id),
        CONSTRAINT CK_fn_notifications_profile_version_Number CHECK (VersionNumber > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知渠道配置版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'AdapterVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'适配器版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'AdapterVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'NonSecretConfigJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'非密钥配置(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'NonSecretConfigJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'ProfileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'ProfileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'ProviderTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'ProviderTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'PublishedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'PublishedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'SecretReference', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密钥引用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'SecretReference';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_provider_profile_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_provider_profile_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_provider_profile_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
    CREATE UNIQUE INDEX UX_fn_notifications_profile_version_Number
        ON dbo.fn_notifications_provider_profile_version(ProfileId, VersionNumber);
    CREATE UNIQUE INDEX UX_fn_notifications_profile_version_Hash
        ON dbo.fn_notifications_provider_profile_version(ProfileId, ContentHash);
END;

IF OBJECT_ID(N'dbo.fn_notifications_binding', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_binding
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BindingKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DraftDispatchModeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DraftJson nvarchar(max) NOT NULL,
        DraftRevision bigint NOT NULL,
        LatestPublishedVersionId uniqueidentifier NULL,
        CreatedById uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_notifications_binding_Version DEFAULT (1),
        CONSTRAINT PK_fn_notifications_binding PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_notifications_binding_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_notifications_binding_Mode CHECK (DraftDispatchModeKey IN ('single', 'fan_out', 'failover', 'match')),
        CONSTRAINT CK_fn_notifications_binding_Revision CHECK (DraftRevision > 0),
        CONSTRAINT CK_fn_notifications_binding_Version CHECK (Version > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知场景绑定表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'BindingKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'场景绑定稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'BindingKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'DraftDispatchModeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿路由模式键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'DraftDispatchModeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'DraftJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'DraftJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'DraftRevision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'DraftRevision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'LatestPublishedVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最新已发布版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'LatestPublishedVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_notifications_binding_Scope_Key
        ON dbo.fn_notifications_binding(TenantScopeKey, BindingKey);
END;

IF OBJECT_ID(N'dbo.fn_notifications_binding_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_binding_version
    (
        Id uniqueidentifier NOT NULL,
        BindingId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        ProducerKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SceneKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ChannelKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DispatchModeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BindingTargetsJson nvarchar(max) NOT NULL,
        ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublishedById uniqueidentifier NOT NULL,
        PublishedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_binding_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_binding_version_Binding FOREIGN KEY (BindingId) REFERENCES dbo.fn_notifications_binding(Id),
        CONSTRAINT CK_fn_notifications_binding_version_Number CHECK (VersionNumber > 0),
        CONSTRAINT CK_fn_notifications_binding_version_Mode CHECK (DispatchModeKey IN ('single', 'fan_out', 'failover', 'match'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知场景绑定版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'BindingId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'场景绑定标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'BindingId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'BindingTargetsJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'绑定目标(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'BindingTargetsJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'ChannelKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'ChannelKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'DispatchModeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'路由模式键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'DispatchModeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'ProducerKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务生产者键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'ProducerKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'PublishedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'PublishedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'SceneKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务场景键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'SceneKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_binding_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_binding_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_binding_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
    CREATE UNIQUE INDEX UX_fn_notifications_binding_version_Number
        ON dbo.fn_notifications_binding_version(BindingId, VersionNumber);
    CREATE UNIQUE INDEX UX_fn_notifications_binding_version_Hash
        ON dbo.fn_notifications_binding_version(BindingId, ContentHash);
END;

IF OBJECT_ID(N'dbo.fn_notifications_intent', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_intent
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProducerKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SceneKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IdempotencyKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TemplateVersionId uniqueidentifier NOT NULL,
        BindingVersionId uniqueidentifier NULL,
        PolicyCategoryKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DispatchModeKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RouteSnapshotJson nvarchar(max) NOT NULL,
        ParameterSnapshotJson nvarchar(max) NOT NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedById uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        Revision bigint NOT NULL CONSTRAINT DF_fn_notifications_intent_Revision DEFAULT (1),
        CONSTRAINT PK_fn_notifications_intent PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_intent_TemplateVersion FOREIGN KEY (TemplateVersionId) REFERENCES dbo.fn_notifications_template_version(Id),
        CONSTRAINT FK_fn_notifications_intent_BindingVersion FOREIGN KEY (BindingVersionId) REFERENCES dbo.fn_notifications_binding_version(Id),
        CONSTRAINT CK_fn_notifications_intent_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_notifications_intent_Policy CHECK (PolicyCategoryKey IN ('mandatory', 'transactional', 'informational', 'marketing')),
        CONSTRAINT CK_fn_notifications_intent_Mode CHECK (DispatchModeKey IN ('single', 'fan_out', 'failover', 'match')),
        CONSTRAINT CK_fn_notifications_intent_Revision CHECK (Revision > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知意图表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'BindingVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'场景绑定版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'BindingVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'DispatchModeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'路由模式键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'DispatchModeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'IdempotencyKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'ParameterSnapshotJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'参数快照(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'ParameterSnapshotJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'PolicyCategoryKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'政策类别键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'PolicyCategoryKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'ProducerKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务生产者键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'ProducerKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'Revision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'Revision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'RouteSnapshotJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'路由快照(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'RouteSnapshotJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'SceneKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务场景键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'SceneKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'意图状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'TemplateVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模板版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'TemplateVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    CREATE CLUSTERED INDEX IX_fn_notifications_intent_Created
        ON dbo.fn_notifications_intent(CreatedAtUtc, Id);
    CREATE UNIQUE INDEX UX_fn_notifications_intent_Idempotency
        ON dbo.fn_notifications_intent(TenantScopeKey, ProducerKey, IdempotencyKey);
END;

IF OBJECT_ID(N'dbo.fn_notifications_recipient', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_recipient
    (
        Id uniqueidentifier NOT NULL,
        IntentId uniqueidentifier NOT NULL,
        RecipientTypeKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RecipientKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        UserId uniqueidentifier NULL,
        AddressDigest char(64) COLLATE Latin1_General_100_BIN2 NULL,
        ResolutionStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_recipient PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_recipient_Intent FOREIGN KEY (IntentId) REFERENCES dbo.fn_notifications_intent(Id),
        CONSTRAINT CK_fn_notifications_recipient_Status CHECK (ResolutionStatusKey IN ('pending', 'resolved', 'failed'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知收件人表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient'), N'AddressDigest', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'地址摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient', @level2type=N'COLUMN', @level2name=N'AddressDigest';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient'), N'IntentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知意图标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient', @level2type=N'COLUMN', @level2name=N'IntentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient'), N'RecipientKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'收件人稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient', @level2type=N'COLUMN', @level2name=N'RecipientKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient'), N'RecipientTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'收件人类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient', @level2type=N'COLUMN', @level2name=N'RecipientTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient'), N'ResolutionStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'解析状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient', @level2type=N'COLUMN', @level2name=N'ResolutionStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient', @level2type=N'COLUMN', @level2name=N'UserId';
    CREATE UNIQUE INDEX UX_fn_notifications_recipient_Intent_Key
        ON dbo.fn_notifications_recipient(IntentId, RecipientTypeKey, RecipientKey);
END;

IF OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_recipient_endpoint
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        UserId uniqueidentifier NOT NULL,
        ProviderProfileVersionId uniqueidentifier NOT NULL,
        EndpointKindKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProtectedValue nvarchar(1024) NOT NULL,
        MaskedValue nvarchar(128) NOT NULL,
        VerificationStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NULL,
        CONSTRAINT PK_fn_notifications_recipient_endpoint PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_endpoint_ProfileVersion FOREIGN KEY (ProviderProfileVersionId) REFERENCES dbo.fn_notifications_provider_profile_version(Id),
        CONSTRAINT CK_fn_notifications_endpoint_ScopeKey CHECK (ScopeKey IN ('host', 'tenant'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知收件端点表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'EndpointKindKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'端点类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'EndpointKindKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'MaskedValue', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'脱敏后的端点值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'MaskedValue';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'ProtectedValue', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的端点原值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'ProtectedValue';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'ProviderProfileVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道配置版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'ProviderProfileVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint'), N'VerificationStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'端点验证状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_recipient_endpoint', @level2type=N'COLUMN', @level2name=N'VerificationStatusKey';
    CREATE UNIQUE INDEX UX_fn_notifications_endpoint_Scope_User_Profile_Kind
        ON dbo.fn_notifications_recipient_endpoint(TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey);
END;

IF OBJECT_ID(N'dbo.fn_notifications_preference', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_preference
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        UserId uniqueidentifier NOT NULL,
        ChannelKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ChannelOptedOut bit NOT NULL CONSTRAINT DF_fn_notifications_preference_OptOut DEFAULT (0),
        MarketingConsentGranted bit NOT NULL CONSTRAINT DF_fn_notifications_preference_Consent DEFAULT (0),
        QuietHoursJson nvarchar(max) NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_notifications_preference_Version DEFAULT (1),
        CONSTRAINT PK_fn_notifications_preference PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_notifications_preference_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_notifications_preference_Version CHECK (Version > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知偏好表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'ChannelKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'ChannelKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'ChannelOptedOut', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否关闭该渠道', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'ChannelOptedOut';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'MarketingConsentGranted', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否授予营销同意', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'MarketingConsentGranted';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'QuietHoursJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'静默时段(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'QuietHoursJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_preference'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_preference', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_notifications_preference_Scope_User_Channel
        ON dbo.fn_notifications_preference(TenantScopeKey, UserId, ChannelKey);
END;

IF OBJECT_ID(N'dbo.fn_notifications_delivery', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_delivery
    (
        Id uniqueidentifier NOT NULL,
        IntentId uniqueidentifier NOT NULL,
        RecipientId uniqueidentifier NOT NULL,
        ChannelKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProviderProfileVersionId uniqueidentifier NULL,
        BindingVersionId uniqueidentifier NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Revision bigint NOT NULL CONSTRAINT DF_fn_notifications_delivery_Revision DEFAULT (1),
        LeaseOwnerKey nvarchar(128) NULL,
        LeaseExpiresAtUtc datetime2(6) NULL,
        LeaseGeneration bigint NOT NULL CONSTRAINT DF_fn_notifications_delivery_LeaseGen DEFAULT (1),
        NextAttemptAtUtc datetime2(6) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NULL,
        CONSTRAINT PK_fn_notifications_delivery PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_delivery_Intent FOREIGN KEY (IntentId) REFERENCES dbo.fn_notifications_intent(Id),
        CONSTRAINT FK_fn_notifications_delivery_Recipient FOREIGN KEY (RecipientId) REFERENCES dbo.fn_notifications_recipient(Id),
        CONSTRAINT FK_fn_notifications_delivery_ProfileVersion FOREIGN KEY (ProviderProfileVersionId) REFERENCES dbo.fn_notifications_provider_profile_version(Id),
        CONSTRAINT FK_fn_notifications_delivery_BindingVersion FOREIGN KEY (BindingVersionId) REFERENCES dbo.fn_notifications_binding_version(Id),
        CONSTRAINT CK_fn_notifications_delivery_Status CHECK (StatusKey IN ('persisted', 'accepted', 'sent', 'delivered', 'unknown', 'read', 'failed', 'suppressed', 'dead_lettered')),
        CONSTRAINT CK_fn_notifications_delivery_Revision CHECK (Revision > 0),
        CONSTRAINT CK_fn_notifications_delivery_LeaseGen CHECK (LeaseGeneration > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知渠道投递表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'BindingVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'场景绑定版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'BindingVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'ChannelKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'ChannelKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'IntentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知意图标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'IntentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'LeaseExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'LeaseGeneration', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约世代', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'LeaseGeneration';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'LeaseOwnerKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约持有者键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'LeaseOwnerKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'NextAttemptAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下次重试时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'NextAttemptAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'ProviderProfileVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道配置版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'ProviderProfileVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'RecipientId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'收件人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'RecipientId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'Revision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'Revision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'投递状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    CREATE CLUSTERED INDEX IX_fn_notifications_delivery_Created
        ON dbo.fn_notifications_delivery(CreatedAtUtc, Id);
    CREATE UNIQUE INDEX UX_fn_notifications_delivery_Recipient_Channel_Profile
        ON dbo.fn_notifications_delivery(RecipientId, ChannelKey, ProviderProfileVersionId);
    CREATE INDEX IX_fn_notifications_delivery_Lease
        ON dbo.fn_notifications_delivery(StatusKey, NextAttemptAtUtc, LeaseExpiresAtUtc);
END;

IF OBJECT_ID(N'dbo.fn_notifications_delivery_attempt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_delivery_attempt
    (
        Id uniqueidentifier NOT NULL,
        DeliveryId uniqueidentifier NOT NULL,
        AttemptNumber int NOT NULL,
        LeaseOwnerKey nvarchar(128) NULL,
        LeaseGeneration bigint NOT NULL,
        LeaseExpiresAtUtc datetime2(6) NULL,
        ResultCategoryKey varchar(32) COLLATE Latin1_General_100_BIN2 NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProviderMessageId nvarchar(128) NULL,
        ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        ReceiptDigest char(64) COLLATE Latin1_General_100_BIN2 NULL,
        StartedAtUtc datetime2(6) NOT NULL,
        FinishedAtUtc datetime2(6) NULL,
        CONSTRAINT PK_fn_notifications_delivery_attempt PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_attempt_Delivery FOREIGN KEY (DeliveryId) REFERENCES dbo.fn_notifications_delivery(Id),
        CONSTRAINT CK_fn_notifications_attempt_Number CHECK (AttemptNumber > 0),
        CONSTRAINT CK_fn_notifications_attempt_LeaseGen CHECK (LeaseGeneration > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知投递尝试表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'AttemptNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'尝试序号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'AttemptNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'DeliveryId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'投递标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'DeliveryId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'FinishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结束时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'FinishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'LeaseExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'LeaseGeneration', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约世代', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'LeaseGeneration';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'LeaseOwnerKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约持有者键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'LeaseOwnerKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'ProviderMessageId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'厂商消息标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'ProviderMessageId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'ReceiptDigest', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'回执摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'ReceiptDigest';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'ResultCategoryKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果类别键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'ResultCategoryKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'StartedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery_attempt'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'尝试状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery_attempt', @level2type=N'COLUMN', @level2name=N'StatusKey';
    CREATE CLUSTERED INDEX IX_fn_notifications_attempt_Started
        ON dbo.fn_notifications_delivery_attempt(StartedAtUtc, Id);
    CREATE UNIQUE INDEX UX_fn_notifications_attempt_Delivery_Number
        ON dbo.fn_notifications_delivery_attempt(DeliveryId, AttemptNumber);
END;

IF OBJECT_ID(N'dbo.fn_notifications_receipt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_receipt
    (
        Id uniqueidentifier NOT NULL,
        ProviderTypeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProviderMessageId nvarchar(128) NULL,
        ReceiptIdempotencyKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DeliveryId uniqueidentifier NULL,
        ExternalStatusKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        MappedStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PayloadDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ReceivedAtUtc datetime2(6) NOT NULL,
        ProcessedAtUtc datetime2(6) NULL,
        ProcessStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CONSTRAINT PK_fn_notifications_receipt PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_receipt_Delivery FOREIGN KEY (DeliveryId) REFERENCES dbo.fn_notifications_delivery(Id),
        CONSTRAINT CK_fn_notifications_receipt_Mapped CHECK (MappedStatusKey IN ('persisted', 'accepted', 'sent', 'delivered', 'unknown', 'read', 'failed', 'suppressed', 'dead_lettered'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知投递回执表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'DeliveryId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'投递标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'DeliveryId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'ExternalStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'外部回执状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'ExternalStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'MappedStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'映射后的投递状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'MappedStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'PayloadDigest', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'载荷摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'PayloadDigest';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'ProcessStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'回执处理状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'ProcessStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'ProcessedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'处理完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'ProcessedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'ProviderMessageId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'厂商消息标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'ProviderMessageId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'ProviderTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'ProviderTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'ReceiptIdempotencyKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'回执幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'ReceiptIdempotencyKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_receipt'), N'ReceivedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接收时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_receipt', @level2type=N'COLUMN', @level2name=N'ReceivedAtUtc';
    CREATE CLUSTERED INDEX IX_fn_notifications_receipt_Received
        ON dbo.fn_notifications_receipt(ReceivedAtUtc, Id);
    CREATE UNIQUE INDEX UX_fn_notifications_receipt_Idempotency
        ON dbo.fn_notifications_receipt(ProviderTypeKey, ReceiptIdempotencyKey);
END;

IF OBJECT_ID(N'dbo.fn_notifications_domain_audit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_domain_audit
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        IntentId uniqueidentifier NULL,
        OperationKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        ResourceTypeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ResourceId uniqueidentifier NOT NULL,
        OutcomeKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DetailJson nvarchar(max) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_domain_audit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_domain_audit_Intent FOREIGN KEY (IntentId) REFERENCES dbo.fn_notifications_intent(Id),
        CONSTRAINT CK_fn_notifications_domain_audit_ScopeKey CHECK (ScopeKey IN ('host', 'tenant'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知领域审计表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'DetailJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审计详情(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'DetailJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'IntentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知意图标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'IntentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'OperationKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'OperationKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'OutcomeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作结果键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'OutcomeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'ResourceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'资源标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'ResourceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'ResourceTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'资源类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'ResourceTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_domain_audit'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_domain_audit', @level2type=N'COLUMN', @level2name=N'TenantId';
    CREATE CLUSTERED INDEX IX_fn_notifications_domain_audit_Created
        ON dbo.fn_notifications_domain_audit(CreatedAtUtc, Id);
    CREATE INDEX IX_fn_notifications_domain_audit_Resource
        ON dbo.fn_notifications_domain_audit(ResourceTypeKey, ResourceId, CreatedAtUtc);
END;

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_fn_notifications_template_version_Immutable
ON dbo.fn_notifications_template_version
INSTEAD OF UPDATE
AS
    THROW 51104, ''Published notification template versions are immutable.'', 1;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_fn_notifications_profile_version_Immutable
ON dbo.fn_notifications_provider_profile_version
INSTEAD OF UPDATE
AS
    THROW 51105, ''Published notification provider profile versions are immutable.'', 1;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_fn_notifications_binding_version_Immutable
ON dbo.fn_notifications_binding_version
INSTEAD OF UPDATE
AS
    THROW 51106, ''Published notification binding versions are immutable.'', 1;');
