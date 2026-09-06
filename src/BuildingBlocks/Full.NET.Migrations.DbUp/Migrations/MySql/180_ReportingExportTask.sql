-- 180：Reporting 导出任务表。

CREATE TABLE IF NOT EXISTS fn_reporting_export_task
(
    Id char(36) NOT NULL,
    TenantId char(36) NOT NULL,
    DefinitionId char(36) NOT NULL,
    VersionNumber int NOT NULL,
    DefinitionKey varchar(128) NOT NULL,
    DefinitionName varchar(128) NOT NULL,
    FormatKey varchar(32) NOT NULL,
    ParametersJson longtext NOT NULL,
    StatusKey varchar(32) NOT NULL,
    OutputFileId char(36) NULL,
    OutputFileName varchar(260) NULL,
    RowCount int NOT NULL DEFAULT 0,
    ErrorCode varchar(128) NULL,
    ErrorMessage varchar(1024) NULL,
    RequestedByUserId char(36) NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CompletedAtUtc datetime(6) NULL,
    Version bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_reporting_export_task_StatusKey
        CHECK (StatusKey IN ('processing', 'succeeded', 'failed')),
    CONSTRAINT CK_fn_reporting_export_task_FormatKey
        CHECK (FormatKey IN ('excel')),
    CONSTRAINT CK_fn_reporting_export_task_VersionNumber CHECK (VersionNumber > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IX_fn_reporting_export_task_TenantId_CreatedAtUtc
    ON fn_reporting_export_task (TenantId, CreatedAtUtc DESC, Id);

CREATE INDEX IX_fn_reporting_export_task_DefinitionId
    ON fn_reporting_export_task (TenantId, DefinitionId, CreatedAtUtc DESC);
