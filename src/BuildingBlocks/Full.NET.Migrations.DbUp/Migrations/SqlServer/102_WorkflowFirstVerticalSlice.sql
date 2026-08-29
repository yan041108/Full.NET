-- 102：建立 Workflow 首个纵向切片的模块自有数据模型与并发不变量。
IF OBJECT_ID(N'dbo.fn_workflow_definition', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_definition
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DefinitionKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DraftId uniqueidentifier NULL,
        LatestPublishedVersionId uniqueidentifier NULL,
        CreatedById uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_workflow_definition_Version DEFAULT (1),
        CONSTRAINT PK_fn_workflow_definition PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_workflow_definition_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_workflow_definition_Version CHECK (Version > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流定义表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'DefinitionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程定义稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'DefinitionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'DraftId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前草稿标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'DraftId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'LatestPublishedVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最新已发布版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'LatestPublishedVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_workflow_definition_Scope_DefinitionKey
        ON dbo.fn_workflow_definition(TenantScopeKey, DefinitionKey);
END;

IF OBJECT_ID(N'dbo.fn_workflow_definition_draft', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_definition_draft
    (
        Id uniqueidentifier NOT NULL,
        DefinitionId uniqueidentifier NOT NULL,
        DraftJson nvarchar(max) NOT NULL,
        DraftRevision bigint NOT NULL,
        ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        UpdatedById uniqueidentifier NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_workflow_definition_draft PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_definition_draft_Definition FOREIGN KEY (DefinitionId) REFERENCES dbo.fn_workflow_definition(Id),
        CONSTRAINT CK_fn_workflow_definition_draft_Revision CHECK (DraftRevision > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_draft')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流定义草稿表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_draft';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_draft')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_draft'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_draft', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_draft')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_draft'), N'DefinitionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_draft', @level2type=N'COLUMN', @level2name=N'DefinitionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_draft')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_draft'), N'DraftJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程定义草稿(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_draft', @level2type=N'COLUMN', @level2name=N'DraftJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_draft')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_draft'), N'DraftRevision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_draft', @level2type=N'COLUMN', @level2name=N'DraftRevision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_draft')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_draft'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_draft', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_draft')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_draft'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_draft', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_draft')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_draft'), N'UpdatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_draft', @level2type=N'COLUMN', @level2name=N'UpdatedById';
    CREATE UNIQUE INDEX UX_fn_workflow_definition_draft_DefinitionId
        ON dbo.fn_workflow_definition_draft(DefinitionId);
END;

IF OBJECT_ID(N'dbo.fn_workflow_definition_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_definition_version
    (
        Id uniqueidentifier NOT NULL,
        DefinitionId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        SchemaVersion int NOT NULL,
        CanonicalJson nvarchar(max) NOT NULL,
        ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublishedById uniqueidentifier NOT NULL,
        PublishedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_workflow_definition_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_definition_version_Definition FOREIGN KEY (DefinitionId) REFERENCES dbo.fn_workflow_definition(Id),
        CONSTRAINT CK_fn_workflow_definition_version_Number CHECK (VersionNumber > 0),
        CONSTRAINT CK_fn_workflow_definition_version_Schema CHECK (SchemaVersion > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流定义发布版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'CanonicalJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规范化流程定义(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'CanonicalJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'DefinitionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'DefinitionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'PublishedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'PublishedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'SchemaVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定义结构版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_definition_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_definition_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
    CREATE UNIQUE INDEX UX_fn_workflow_definition_version_Definition_Number
        ON dbo.fn_workflow_definition_version(DefinitionId, VersionNumber);
    CREATE UNIQUE INDEX UX_fn_workflow_definition_version_Definition_Hash
        ON dbo.fn_workflow_definition_version(DefinitionId, ContentHash);
END;

IF OBJECT_ID(N'dbo.fn_workflow_form_definition', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_form_definition
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FormKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DraftSchemaJson nvarchar(max) NOT NULL,
        DraftRevision bigint NOT NULL,
        LatestPublishedVersionId uniqueidentifier NULL,
        CreatedById uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NULL,
        CONSTRAINT PK_fn_workflow_form_definition PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_workflow_form_definition_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_workflow_form_definition_Revision CHECK (DraftRevision > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流表单定义表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'DraftRevision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'草稿修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'DraftRevision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'DraftSchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单草稿结构(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'DraftSchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'FormKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'FormKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'LatestPublishedVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最新已发布版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'LatestPublishedVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_definition'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_definition', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    CREATE UNIQUE INDEX UX_fn_workflow_form_definition_Scope_FormKey
        ON dbo.fn_workflow_form_definition(TenantScopeKey, FormKey);
END;

IF OBJECT_ID(N'dbo.fn_workflow_form_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_form_version
    (
        Id uniqueidentifier NOT NULL,
        FormDefinitionId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        SchemaVersion int NOT NULL,
        AdapterVersion int NOT NULL,
        ComponentCatalogVersion int NOT NULL,
        FormSchemaJson nvarchar(max) NOT NULL,
        WebRenderSchemaJson nvarchar(max) NOT NULL,
        ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublishedById uniqueidentifier NOT NULL,
        PublishedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_workflow_form_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_form_version_Definition FOREIGN KEY (FormDefinitionId) REFERENCES dbo.fn_workflow_form_definition(Id),
        CONSTRAINT CK_fn_workflow_form_version_Number CHECK (VersionNumber > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流表单发布版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'AdapterVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单适配器版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'AdapterVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'ComponentCatalogVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组件目录版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'ComponentCatalogVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'FormDefinitionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'FormDefinitionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'FormSchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单结构(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'FormSchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'PublishedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'PublishedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'SchemaVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单结构版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_version'), N'WebRenderSchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Web 渲染结构(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_version', @level2type=N'COLUMN', @level2name=N'WebRenderSchemaJson';
    CREATE UNIQUE INDEX UX_fn_workflow_form_version_Definition_Number
        ON dbo.fn_workflow_form_version(FormDefinitionId, VersionNumber);
    CREATE UNIQUE INDEX UX_fn_workflow_form_version_Definition_Hash
        ON dbo.fn_workflow_form_version(FormDefinitionId, ContentHash);
END;

IF OBJECT_ID(N'dbo.fn_workflow_instance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_instance
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DefinitionVersionId uniqueidentifier NOT NULL,
        FormVersionId uniqueidentifier NULL,
        BusinessType nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BusinessId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        StatusKey varchar(24) NOT NULL,
        Revision bigint NOT NULL,
        StartedById uniqueidentifier NOT NULL,
        StartedAtUtc datetime2(6) NOT NULL,
        CompletedAtUtc datetime2(6) NULL,
        CancelledById uniqueidentifier NULL,
        CancelledAtUtc datetime2(6) NULL,
        CancellationReason nvarchar(512) NULL,
        LeaseOwnerKey nvarchar(128) NULL,
        LeaseExpiresAtUtc datetime2(6) NULL,
        ActiveBusinessKey AS (CASE WHEN StatusKey = 'active' THEN CONCAT(TenantScopeKey, N'|', BusinessType, N'|', BusinessId) END) PERSISTED,
        CONSTRAINT PK_fn_workflow_instance PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_instance_DefinitionVersion FOREIGN KEY (DefinitionVersionId) REFERENCES dbo.fn_workflow_definition_version(Id),
        CONSTRAINT FK_fn_workflow_instance_FormVersion FOREIGN KEY (FormVersionId) REFERENCES dbo.fn_workflow_form_version(Id),
        CONSTRAINT CK_fn_workflow_instance_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_workflow_instance_Status CHECK (StatusKey IN ('active', 'completed', 'rejected', 'cancelled', 'suspended')),
        CONSTRAINT CK_fn_workflow_instance_Revision CHECK (Revision > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流实例表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'BusinessId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务对象标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'BusinessId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'BusinessType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务对象类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'BusinessType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'CancellationReason', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'取消原因', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'CancellationReason';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'CancelledAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'取消时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'CancelledAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'CancelledById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'取消人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'CancelledById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'DefinitionVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程定义版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'DefinitionVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'FormVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'FormVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'LeaseExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'LeaseOwnerKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行租约持有者键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'LeaseOwnerKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'Revision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'Revision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'StartedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'StartedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发起人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'StartedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    CREATE UNIQUE INDEX UX_fn_workflow_instance_ActiveBusinessKey
        ON dbo.fn_workflow_instance(ActiveBusinessKey) WHERE StatusKey = 'active';
    CREATE INDEX IX_fn_workflow_instance_Scope_Status
        ON dbo.fn_workflow_instance(TenantScopeKey, StatusKey, StartedAtUtc);
END;

IF OBJECT_ID(N'dbo.fn_workflow_step', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_step
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        NodeKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        NodeTypeKey varchar(64) NOT NULL,
        StatusKey varchar(24) NOT NULL,
        AssignedUserId uniqueidentifier NULL,
        DueAtUtc datetime2(6) NULL,
        AttemptCount int NOT NULL CONSTRAINT DF_fn_workflow_step_AttemptCount DEFAULT (0),
        Revision bigint NOT NULL,
        StartedAtUtc datetime2(6) NOT NULL,
        CompletedAtUtc datetime2(6) NULL,
        CONSTRAINT PK_fn_workflow_step PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_step_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT CK_fn_workflow_step_Revision CHECK (Revision > 0),
        CONSTRAINT CK_fn_workflow_step_Attempts CHECK (AttemptCount >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流步骤表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'AssignedUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前处理人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'AssignedUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'AttemptCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'AttemptCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'DueAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'处理截止时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'DueAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'NodeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程节点键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'NodeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'NodeTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程节点类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'NodeTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'Revision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'步骤修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'Revision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'StartedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'步骤状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'StatusKey';
    CREATE INDEX IX_fn_workflow_step_Instance_Status ON dbo.fn_workflow_step(InstanceId, StatusKey);
END;

IF OBJECT_ID(N'dbo.fn_workflow_todo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_todo
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NOT NULL,
        AssigneeUserId uniqueidentifier NOT NULL,
        StatusKey varchar(24) NOT NULL,
        Revision bigint NOT NULL,
        ArrivedAtUtc datetime2(6) NOT NULL,
        CompletedAtUtc datetime2(6) NULL,
        ResultActionKey varchar(32) NULL,
        CONSTRAINT PK_fn_workflow_todo PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_todo_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_todo_Step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step(Id),
        CONSTRAINT CK_fn_workflow_todo_Revision CHECK (Revision > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流待办表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'ArrivedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'待办到达时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'ArrivedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'AssigneeUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'待办处理人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'AssigneeUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'ResultActionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'处理结果动作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'ResultActionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'Revision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'待办修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'Revision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'待办状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_todo')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo'), N'StepId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo', @level2type=N'COLUMN', @level2name=N'StepId';
    CREATE INDEX IX_fn_workflow_todo_Assignee_Status ON dbo.fn_workflow_todo(AssigneeUserId, StatusKey, ArrivedAtUtc);
    CREATE UNIQUE INDEX UX_fn_workflow_todo_Step_Assignee ON dbo.fn_workflow_todo(StepId, AssigneeUserId);
END;

IF OBJECT_ID(N'dbo.fn_workflow_cc', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_cc
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NULL,
        RecipientUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        ReadAtUtc datetime2(6) NULL,
        CONSTRAINT PK_fn_workflow_cc PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_cc_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_cc_Step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_cc')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流抄送记录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_cc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_cc')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_cc'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_cc', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_cc')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_cc'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_cc', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_cc')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_cc'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_cc', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_cc')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_cc'), N'ReadAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已读时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_cc', @level2type=N'COLUMN', @level2name=N'ReadAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_cc')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_cc'), N'RecipientUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接收人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_cc', @level2type=N'COLUMN', @level2name=N'RecipientUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_cc')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_cc'), N'StepId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_cc', @level2type=N'COLUMN', @level2name=N'StepId';
    CREATE UNIQUE INDEX UX_fn_workflow_cc_Instance_Recipient ON dbo.fn_workflow_cc(InstanceId, RecipientUserId);
END;

IF OBJECT_ID(N'dbo.fn_workflow_form_submission', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_form_submission
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        FormVersionId uniqueidentifier NOT NULL,
        SubmissionJson nvarchar(max) NOT NULL,
        DataClassificationSummary nvarchar(512) NOT NULL,
        Revision bigint NOT NULL,
        UpdatedById uniqueidentifier NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_workflow_form_submission PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_form_submission_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_form_submission_FormVersion FOREIGN KEY (FormVersionId) REFERENCES dbo.fn_workflow_form_version(Id),
        CONSTRAINT CK_fn_workflow_form_submission_Revision CHECK (Revision > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流表单提交表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission'), N'DataClassificationSummary', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据分级摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission', @level2type=N'COLUMN', @level2name=N'DataClassificationSummary';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission'), N'FormVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission', @level2type=N'COLUMN', @level2name=N'FormVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission'), N'Revision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提交修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission', @level2type=N'COLUMN', @level2name=N'Revision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission'), N'SubmissionJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单提交数据(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission', @level2type=N'COLUMN', @level2name=N'SubmissionJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_form_submission')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_form_submission'), N'UpdatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_form_submission', @level2type=N'COLUMN', @level2name=N'UpdatedById';
    CREATE UNIQUE INDEX UX_fn_workflow_form_submission_Instance ON dbo.fn_workflow_form_submission(InstanceId);
END;

IF OBJECT_ID(N'dbo.fn_workflow_action_record', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_action_record
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NULL,
        TodoId uniqueidentifier NULL,
        ActionKey varchar(32) NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        InstanceRevision bigint NOT NULL,
        IdempotencyKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CommentSummary nvarchar(512) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_workflow_action_record PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_action_record_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_action_record_Step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step(Id),
        CONSTRAINT FK_fn_workflow_action_record_Todo FOREIGN KEY (TodoId) REFERENCES dbo.fn_workflow_todo(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流动作记录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'ActionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'ActionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'CommentSummary', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'动作意见摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'CommentSummary';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'IdempotencyKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'InstanceRevision', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例修订号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'InstanceRevision';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'StepId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'StepId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_action_record')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_action_record'), N'TodoId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_action_record', @level2type=N'COLUMN', @level2name=N'TodoId';
    CREATE UNIQUE INDEX UX_fn_workflow_action_record_Instance_Idempotency
        ON dbo.fn_workflow_action_record(InstanceId, IdempotencyKey);
END;

IF OBJECT_ID(N'dbo.fn_workflow_execution_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_execution_log
    (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        StepId uniqueidentifier NULL,
        TransitionKey varchar(64) NOT NULL,
        FromStatusKey varchar(24) NULL,
        ToStatusKey varchar(24) NOT NULL,
        IdempotencyKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        Summary nvarchar(1024) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_workflow_execution_log PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_execution_log_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT FK_fn_workflow_execution_log_Step FOREIGN KEY (StepId) REFERENCES dbo.fn_workflow_step(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流执行日志表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'FromStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'迁移前状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'FromStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'IdempotencyKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'StepId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'StepId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'Summary', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'Summary';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'ToStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'迁移后状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'ToStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_execution_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_execution_log'), N'TransitionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态迁移键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_execution_log', @level2type=N'COLUMN', @level2name=N'TransitionKey';
    CREATE INDEX IX_fn_workflow_execution_log_Instance_Created ON dbo.fn_workflow_execution_log(InstanceId, CreatedAtUtc);
END;

IF OBJECT_ID(N'dbo.fn_workflow_domain_audit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_domain_audit
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        InstanceId uniqueidentifier NULL,
        OperationKey varchar(64) NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        ResourceTypeKey varchar(64) NOT NULL,
        ResourceId uniqueidentifier NOT NULL,
        OutcomeKey varchar(32) NOT NULL,
        DetailJson nvarchar(max) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_workflow_domain_audit PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_workflow_domain_audit_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT CK_fn_workflow_domain_audit_ScopeKey CHECK (ScopeKey IN ('host', 'tenant'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流领域审计表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'DetailJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'审计详情(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'DetailJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'OperationKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'OperationKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'OutcomeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作结果键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'OutcomeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'ResourceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'资源标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'ResourceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'ResourceTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'资源类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'ResourceTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_workflow_domain_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_domain_audit'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_domain_audit', @level2type=N'COLUMN', @level2name=N'TenantId';
    CREATE INDEX IX_fn_workflow_domain_audit_Resource ON dbo.fn_workflow_domain_audit(ResourceTypeKey, ResourceId, CreatedAtUtc);
END;

-- 发布版本是追加式事实；任何业务列更新都必须失败关闭。
EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_fn_workflow_definition_version_Immutable
ON dbo.fn_workflow_definition_version
INSTEAD OF UPDATE
AS
    THROW 51102, ''Published workflow definition versions are immutable.'', 1;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_fn_workflow_form_version_Immutable
ON dbo.fn_workflow_form_version
INSTEAD OF UPDATE
AS
    THROW 51103, ''Published workflow form versions are immutable.'', 1;');
