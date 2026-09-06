-- 195：GoView 大屏项目与发布版本表。

CREATE TABLE IF NOT EXISTS fn_goview_project
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    ProjectKey varchar(128) COLLATE utf8mb4_bin NOT NULL,
    Name varchar(128) NOT NULL,
    CanvasJson longtext NOT NULL,
    LatestPublishedVersionNumber int NOT NULL DEFAULT 0,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_goview_project_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_goview_project_version
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    ProjectId char(36) COLLATE utf8mb4_bin NOT NULL,
    VersionNumber int NOT NULL,
    CanvasJson longtext NOT NULL,
    ChangeNote varchar(512) NULL,
    PublishedByUserId char(36) COLLATE utf8mb4_bin NOT NULL,
    PublishedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_goview_project_version_VersionNumber CHECK (VersionNumber > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE UNIQUE INDEX UX_fn_goview_project_ProjectKey
    ON fn_goview_project (ProjectKey);

CREATE UNIQUE INDEX UX_fn_goview_project_version_ProjectId_VersionNumber
    ON fn_goview_project_version (ProjectId, VersionNumber);
