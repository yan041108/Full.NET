-- 232：Host 文档版本保留策略数据库覆盖（单行，TenantId IS NULL）。

IF OBJECT_ID(N'dbo.fn_document_version_retention_setting', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_version_retention_setting
    (
        Id uniqueidentifier NOT NULL
            CONSTRAINT PK_fn_document_version_retention_setting PRIMARY KEY,
        TenantId uniqueidentifier NULL,
        MinimumRetainedVersionsPerItem int NOT NULL,
        MaximumRetainedHistoryVersions int NOT NULL,
        PollSeconds int NOT NULL,
        BatchSize int NOT NULL,
        Version bigint NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL
    );

    CREATE UNIQUE INDEX UX_fn_document_version_retention_setting_Host
        ON dbo.fn_document_version_retention_setting (TenantId)
        WHERE TenantId IS NULL;
END;
