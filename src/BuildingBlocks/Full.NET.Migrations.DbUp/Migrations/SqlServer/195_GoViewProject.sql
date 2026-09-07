-- 195：GoView 大屏项目与发布版本表。

IF OBJECT_ID(N'dbo.fn_goview_project', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_goview_project
    (
        Id uniqueidentifier NOT NULL,
        ProjectKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        CanvasJson nvarchar(max) NOT NULL,
        LatestPublishedVersionNumber int NOT NULL
            CONSTRAINT DF_fn_goview_project_LatestPublishedVersionNumber DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_goview_project_IsEnabled DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_goview_project_Version DEFAULT (1),
        CONSTRAINT PK_fn_goview_project PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_goview_project_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'GoView项目表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'CanvasJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Canvas(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'CanvasJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'LatestPublishedVersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最新已发布版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'LatestPublishedVersionNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'ProjectKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'项目稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'ProjectKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_goview_project_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_goview_project_version
    (
        Id uniqueidentifier NOT NULL,
        ProjectId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        CanvasJson nvarchar(max) NOT NULL,
        ChangeNote nvarchar(512) NULL,
        PublishedByUserId uniqueidentifier NOT NULL,
        PublishedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_goview_project_version PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_goview_project_version_VersionNumber CHECK (VersionNumber > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project_version')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'GoView项目版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project_version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project_version'), N'CanvasJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Canvas(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project_version', @level2type=N'COLUMN', @level2name=N'CanvasJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project_version'), N'ChangeNote', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'变更说明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project_version', @level2type=N'COLUMN', @level2name=N'ChangeNote';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project_version'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project_version', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project_version'), N'ProjectId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'项目标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project_version', @level2type=N'COLUMN', @level2name=N'ProjectId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project_version'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project_version', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project_version'), N'PublishedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project_version', @level2type=N'COLUMN', @level2name=N'PublishedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_goview_project_version')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_goview_project_version'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_goview_project_version', @level2type=N'COLUMN', @level2name=N'VersionNumber';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_goview_project')
      AND name = N'UX_fn_goview_project_ProjectKey')
    CREATE UNIQUE INDEX UX_fn_goview_project_ProjectKey
        ON dbo.fn_goview_project(ProjectKey);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_goview_project_version')
      AND name = N'UX_fn_goview_project_version_ProjectId_VersionNumber')
    CREATE UNIQUE INDEX UX_fn_goview_project_version_ProjectId_VersionNumber
        ON dbo.fn_goview_project_version(ProjectId, VersionNumber);
