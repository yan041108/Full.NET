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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'报表分组表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_group'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_group'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_group'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_group'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_group'), N'ParentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'父级标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group', @level2type=N'COLUMN', @level2name=N'ParentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_group'), N'SortOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group', @level2type=N'COLUMN', @level2name=N'SortOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_group'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_group')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_group'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_group', @level2type=N'COLUMN', @level2name=N'Version';
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'报表定义表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'DataSourceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据源标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'DataSourceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'DefinitionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定义稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'DefinitionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'GroupId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分组标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'GroupId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'LatestPublishedVersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最新已发布版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'LatestPublishedVersionNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'LayoutConfigJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Layout Config(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'LayoutConfigJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'ParameterSchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'参数结构(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'ParameterSchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'QueryPortKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'查询端口键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'QueryPortKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition', @level2type=N'COLUMN', @level2name=N'Version';
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'报表定义版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'ChangeNote', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'变更说明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'ChangeNote';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'DataSourceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据源标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'DataSourceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'DefinitionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'DefinitionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'LayoutConfigJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Layout Config(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'LayoutConfigJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'ParameterSchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'参数结构(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'ParameterSchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'PublishedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'PublishedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'QueryPortKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'查询端口键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'QueryPortKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_definition_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_definition_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
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
