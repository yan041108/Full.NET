-- 182：Printing 模板与发布版本表。

IF OBJECT_ID(N'dbo.fn_printing_template', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_printing_template
    (
        Id uniqueidentifier NOT NULL,
        TemplateKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        FormSchemaKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        LayoutHtml nvarchar(max) NOT NULL,
        LatestPublishedVersionNumber int NOT NULL
            CONSTRAINT DF_fn_printing_template_LatestPublishedVersionNumber DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_printing_template_IsEnabled DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_printing_template_Version DEFAULT (1),
        CONSTRAINT PK_fn_printing_template PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_printing_template_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'打印模板表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'FormSchemaKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单结构键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'FormSchemaKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'LatestPublishedVersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最新已发布版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'LatestPublishedVersionNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'LayoutHtml', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'布局 HTML', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'LayoutHtml';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'TemplateKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模板稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'TemplateKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_printing_template_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_printing_template_version
    (
        Id uniqueidentifier NOT NULL,
        TemplateId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        LayoutHtml nvarchar(max) NOT NULL,
        ChangeNote nvarchar(512) NULL,
        PublishedByUserId uniqueidentifier NOT NULL,
        PublishedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_printing_template_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_printing_template_version_VersionNumber CHECK (VersionNumber > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'打印模板版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_version'), N'ChangeNote', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'变更说明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_version', @level2type=N'COLUMN', @level2name=N'ChangeNote';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_version'), N'LayoutHtml', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'布局 HTML', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_version', @level2type=N'COLUMN', @level2name=N'LayoutHtml';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_version'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_version', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_version'), N'PublishedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_version', @level2type=N'COLUMN', @level2name=N'PublishedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_version'), N'TemplateId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模板标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_version', @level2type=N'COLUMN', @level2name=N'TemplateId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_printing_template_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_printing_template')
      AND name = N'UX_fn_printing_template_TemplateKey')
    CREATE UNIQUE INDEX UX_fn_printing_template_TemplateKey
        ON dbo.fn_printing_template(TemplateKey);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_printing_template_version')
      AND name = N'UX_fn_printing_template_version_TemplateId_VersionNumber')
    CREATE UNIQUE INDEX UX_fn_printing_template_version_TemplateId_VersionNumber
        ON dbo.fn_printing_template_version(TemplateId, VersionNumber);
