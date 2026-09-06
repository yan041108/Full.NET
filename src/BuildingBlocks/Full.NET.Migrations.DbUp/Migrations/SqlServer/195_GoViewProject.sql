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
