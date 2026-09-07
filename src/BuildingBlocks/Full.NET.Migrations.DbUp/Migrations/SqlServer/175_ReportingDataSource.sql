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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'报表数据源表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'DatabaseName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据库名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'DatabaseName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'LastTestMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次探测消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'LastTestMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'LastTestStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次探测状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'LastTestStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'LastTestedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last Tested At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'LastTestedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'PasswordProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的密码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'PasswordProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'Port', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'端口', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'Port';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'ServerHost', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'服务器主机', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'ServerHost';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'TrustServerCertificate', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否信任服务器证书', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'TrustServerCertificate';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'Username', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'Username';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_data_source'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_data_source', @level2type=N'COLUMN', @level2name=N'Version';

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
