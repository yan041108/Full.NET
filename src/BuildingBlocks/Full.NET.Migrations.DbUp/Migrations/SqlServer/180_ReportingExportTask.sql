-- 180：Reporting 导出任务表。

IF OBJECT_ID(N'dbo.fn_reporting_export_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_reporting_export_task
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        DefinitionId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        DefinitionKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DefinitionName nvarchar(128) NOT NULL,
        FormatKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ParametersJson nvarchar(max) NOT NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OutputFileId uniqueidentifier NULL,
        OutputFileName nvarchar(260) NULL,
        RowCount int NOT NULL
            CONSTRAINT DF_fn_reporting_export_task_RowCount DEFAULT (0),
        ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        ErrorMessage nvarchar(1024) NULL,
        RequestedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL
            CONSTRAINT DF_fn_reporting_export_task_Version DEFAULT (1),
        CONSTRAINT PK_fn_reporting_export_task PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_reporting_export_task_StatusKey
            CHECK (StatusKey IN (N'processing', N'succeeded', N'failed')),
        CONSTRAINT CK_fn_reporting_export_task_FormatKey
            CHECK (FormatKey IN (N'excel')),
        CONSTRAINT CK_fn_reporting_export_task_VersionNumber CHECK (VersionNumber > 0)
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
      AND name = N'IX_fn_reporting_export_task_TenantId_CreatedAtUtc')
    CREATE INDEX IX_fn_reporting_export_task_TenantId_CreatedAtUtc
        ON dbo.fn_reporting_export_task(TenantId, CreatedAtUtc DESC, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
      AND name = N'IX_fn_reporting_export_task_DefinitionId')
    CREATE INDEX IX_fn_reporting_export_task_DefinitionId
        ON dbo.fn_reporting_export_task(TenantId, DefinitionId, CreatedAtUtc DESC);
