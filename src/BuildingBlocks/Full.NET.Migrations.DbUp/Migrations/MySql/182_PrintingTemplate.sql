-- 182：Printing 模板与发布版本表。

CREATE TABLE IF NOT EXISTS fn_printing_template
(
    Id char(36) NOT NULL,
    TemplateKey varchar(128) NOT NULL,
    Name varchar(128) NOT NULL,
    FormSchemaKey varchar(128) NOT NULL,
    LayoutHtml longtext NOT NULL,
    LatestPublishedVersionNumber int NOT NULL DEFAULT 0,
    IsEnabled bit(1) NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_printing_template_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fn_printing_template_version
(
    Id char(36) NOT NULL,
    TemplateId char(36) NOT NULL,
    VersionNumber int NOT NULL,
    LayoutHtml longtext NOT NULL,
    ChangeNote varchar(512) NULL,
    PublishedByUserId char(36) NOT NULL,
    PublishedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_printing_template_version_VersionNumber CHECK (VersionNumber > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE UNIQUE INDEX UX_fn_printing_template_TemplateKey
    ON fn_printing_template (TemplateKey);

CREATE UNIQUE INDEX UX_fn_printing_template_version_TemplateId_VersionNumber
    ON fn_printing_template_version (TemplateId, VersionNumber);
