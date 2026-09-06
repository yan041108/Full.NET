-- 175：Reporting 报表数据源配置主表。

IF OBJECT_ID(N'dbo.fn_reporting_data_source', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_reporting_data_source
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        Name nvarchar(128) NOT NULL,
        ProviderKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ServerHost nvarchar(256) NOT NULL,
        Port int NOT NULL,
        DatabaseName nvarchar(128) NOT NULL,
        Username nvarchar(128) NOT NULL,
        PasswordProtected nvarchar(max) NOT NULL,
        TrustServerCertificate bit NOT NULL
            CONSTRAINT DF_fn_reporting_data_source_TrustServerCertificate DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_reporting_data_source_IsEnabled DEFAULT (1),
        LastTestedAtUtc datetimeoffset(7) NULL,
        LastTestStatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
        LastTestMessage nvarchar(512) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_reporting_data_source_Version DEFAULT (1),
        CONSTRAINT PK_fn_reporting_data_source PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_reporting_data_source_ProviderKey
            CHECK (ProviderKey IN (N'sql_server', N'mysql')),
        CONSTRAINT CK_fn_reporting_data_source_LastTestStatusKey
            CHECK (LastTestStatusKey IS NULL OR LastTestStatusKey IN (N'succeeded', N'failed'))
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Reporting 报表数据源配置表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
      AND indexObject.name = N'IX_fn_reporting_data_source_IsEnabled_Name'
)
    CREATE INDEX IX_fn_reporting_data_source_IsEnabled_Name
        ON dbo.fn_reporting_data_source(IsEnabled, Name, Id);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
      AND indexObject.name = N'IX_fn_reporting_data_source_TenantId'
)
    CREATE INDEX IX_fn_reporting_data_source_TenantId
        ON dbo.fn_reporting_data_source(TenantId, Name, Id);
