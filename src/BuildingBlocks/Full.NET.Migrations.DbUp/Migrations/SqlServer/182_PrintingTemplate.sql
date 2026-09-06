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
