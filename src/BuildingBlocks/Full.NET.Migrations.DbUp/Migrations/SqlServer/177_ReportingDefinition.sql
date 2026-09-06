-- 177：Reporting 分组、定义与发布版本表。

IF OBJECT_ID(N'dbo.fn_reporting_group', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_reporting_group
    (
        Id uniqueidentifier NOT NULL,
        ParentId uniqueidentifier NULL,
        Name nvarchar(128) NOT NULL,
        SortOrder int NOT NULL
            CONSTRAINT DF_fn_reporting_group_SortOrder DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_reporting_group_IsEnabled DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_reporting_group_Version DEFAULT (1),
        CONSTRAINT PK_fn_reporting_group PRIMARY KEY CLUSTERED (Id)
    );
END;

IF OBJECT_ID(N'dbo.fn_reporting_definition', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_reporting_definition
    (
        Id uniqueidentifier NOT NULL,
        GroupId uniqueidentifier NOT NULL,
        DataSourceId uniqueidentifier NOT NULL,
        DefinitionKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        QueryPortKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ParameterSchemaJson nvarchar(max) NOT NULL,
        LayoutConfigJson nvarchar(max) NOT NULL,
        LatestPublishedVersionNumber int NOT NULL
            CONSTRAINT DF_fn_reporting_definition_LatestPublishedVersionNumber DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_reporting_definition_IsEnabled DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_reporting_definition_Version DEFAULT (1),
        CONSTRAINT PK_fn_reporting_definition PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_reporting_definition_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
    );
END;

IF OBJECT_ID(N'dbo.fn_reporting_definition_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_reporting_definition_version
    (
        Id uniqueidentifier NOT NULL,
        DefinitionId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        DataSourceId uniqueidentifier NOT NULL,
        QueryPortKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ParameterSchemaJson nvarchar(max) NOT NULL,
        LayoutConfigJson nvarchar(max) NOT NULL,
        ChangeNote nvarchar(512) NULL,
        PublishedByUserId uniqueidentifier NOT NULL,
        PublishedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_reporting_definition_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_reporting_definition_version_VersionNumber CHECK (VersionNumber > 0)
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_group')
      AND name = N'IX_fn_reporting_group_ParentId_SortOrder')
    CREATE INDEX IX_fn_reporting_group_ParentId_SortOrder
        ON dbo.fn_reporting_group(ParentId, SortOrder, Name, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_definition')
      AND name = N'UX_fn_reporting_definition_DefinitionKey')
    CREATE UNIQUE INDEX UX_fn_reporting_definition_DefinitionKey
        ON dbo.fn_reporting_definition(DefinitionKey);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_definition')
      AND name = N'IX_fn_reporting_definition_GroupId_Name')
    CREATE INDEX IX_fn_reporting_definition_GroupId_Name
        ON dbo.fn_reporting_definition(GroupId, Name, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
      AND name = N'UX_fn_reporting_definition_version_DefinitionId_VersionNumber')
    CREATE UNIQUE INDEX UX_fn_reporting_definition_version_DefinitionId_VersionNumber
        ON dbo.fn_reporting_definition_version(DefinitionId, VersionNumber);
