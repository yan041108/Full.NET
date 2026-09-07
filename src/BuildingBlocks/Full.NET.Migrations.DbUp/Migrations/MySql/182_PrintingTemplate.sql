-- 182：Printing 模板与发布版本表。

CREATE TABLE IF NOT EXISTS fn_printing_template (
    Id char(36) NOT NULL COMMENT '逻辑主键',
    TemplateKey varchar(128) NOT NULL COMMENT '模板稳定键',
    Name varchar(128) NOT NULL COMMENT '名称',
    FormSchemaKey varchar(128) NOT NULL COMMENT '表单结构键',
    LayoutHtml longtext NOT NULL COMMENT '布局 HTML',
    LatestPublishedVersionNumber int NOT NULL DEFAULT 0 COMMENT '最新已发布版本号',
    IsEnabled bit(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_printing_template_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
) COMMENT='打印模板表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fn_printing_template_version (
    Id char(36) NOT NULL COMMENT '逻辑主键',
    TemplateId char(36) NOT NULL COMMENT '模板标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    LayoutHtml longtext NOT NULL COMMENT '布局 HTML',
    ChangeNote varchar(512) NULL COMMENT '变更说明',
    PublishedByUserId char(36) NOT NULL COMMENT '发布人用户标识',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '发布时间(UTC)',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_printing_template_version_VersionNumber CHECK (VersionNumber > 0)
) COMMENT='打印模板版本表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX UX_fn_printing_template_TemplateKey
    ON fn_printing_template (TemplateKey);

CREATE UNIQUE INDEX UX_fn_printing_template_version_TemplateId_VersionNumber
    ON fn_printing_template_version (TemplateId, VersionNumber);
