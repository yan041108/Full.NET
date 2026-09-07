-- 195：GoView 大屏项目与发布版本表。

CREATE TABLE IF NOT EXISTS fn_goview_project (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    ProjectKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '项目稳定键',
    Name varchar(128) NOT NULL COMMENT '名称',
    CanvasJson longtext NOT NULL COMMENT 'Canvas(JSON)',
    LatestPublishedVersionNumber int NOT NULL DEFAULT 0 COMMENT '最新已发布版本号',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_goview_project_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
) COMMENT='GoView项目表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_goview_project_version (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    ProjectId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '项目标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    CanvasJson longtext NOT NULL COMMENT 'Canvas(JSON)',
    ChangeNote varchar(512) NULL COMMENT '变更说明',
    PublishedByUserId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '发布人用户标识',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '发布时间(UTC)',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_goview_project_version_VersionNumber CHECK (VersionNumber > 0)
) COMMENT='GoView项目版本表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE UNIQUE INDEX UX_fn_goview_project_ProjectKey
    ON fn_goview_project (ProjectKey);

CREATE UNIQUE INDEX UX_fn_goview_project_version_ProjectId_VersionNumber
    ON fn_goview_project_version (ProjectId, VersionNumber);
