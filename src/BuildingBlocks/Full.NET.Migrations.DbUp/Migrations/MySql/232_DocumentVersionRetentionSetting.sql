-- 232：Host 文档版本保留策略数据库覆盖（单行，TenantId IS NULL）。

CREATE TABLE IF NOT EXISTS fn_document_version_retention_setting
(
    Id char(36) NOT NULL,
    TenantId char(36) NULL,
    MinimumRetainedVersionsPerItem int NOT NULL,
    MaximumRetainedHistoryVersions int NOT NULL,
    PollSeconds int NOT NULL,
    BatchSize int NOT NULL,
    Version bigint NOT NULL,
    UpdatedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id)
);
