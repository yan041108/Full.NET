-- 180：Reporting 导出任务表。

CREATE TABLE IF NOT EXISTS fn_reporting_export_task (
    Id char(36) NOT NULL COMMENT '逻辑主键',
    TenantId char(36) NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    DefinitionId char(36) NOT NULL COMMENT '定义标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    DefinitionKey varchar(128) NOT NULL COMMENT '定义稳定键',
    DefinitionName varchar(128) NOT NULL COMMENT '定义名称',
    FormatKey varchar(32) NOT NULL COMMENT '格式键',
    ParametersJson longtext NOT NULL COMMENT 'Parameters(JSON)',
    StatusKey varchar(32) NOT NULL COMMENT '状态键',
    OutputFileId char(36) NULL COMMENT '输出文件标识',
    OutputFileName varchar(260) NULL COMMENT '输出文件名',
    RowCount int NOT NULL DEFAULT 0 COMMENT '行数',
    ErrorCode varchar(128) NULL COMMENT '错误码',
    ErrorMessage varchar(1024) NULL COMMENT '错误消息',
    RequestedByUserId char(36) NOT NULL COMMENT '请求人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_reporting_export_task_StatusKey
        CHECK (StatusKey IN ('processing', 'succeeded', 'failed')),
    CONSTRAINT CK_fn_reporting_export_task_FormatKey
        CHECK (FormatKey IN ('excel')),
    CONSTRAINT CK_fn_reporting_export_task_VersionNumber CHECK (VersionNumber > 0)
) COMMENT='报表导出任务表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IX_fn_reporting_export_task_TenantId_CreatedAtUtc
    ON fn_reporting_export_task (TenantId, CreatedAtUtc DESC, Id);

CREATE INDEX IX_fn_reporting_export_task_DefinitionId
    ON fn_reporting_export_task (TenantId, DefinitionId, CreatedAtUtc DESC);
