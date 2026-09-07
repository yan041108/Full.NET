-- 184：AI 模型配置与租户配额表。

IF OBJECT_ID(N'dbo.fn_ai_model_config', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_model_config
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        Name nvarchar(128) NOT NULL,
        ProviderKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        EndpointBaseUrl nvarchar(512) NOT NULL,
        ModelId varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ApiKeyProtected nvarchar(max) NULL,
        OrganizationId varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        IsDefault bit NOT NULL
            CONSTRAINT DF_fn_ai_model_config_IsDefault DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_ai_model_config_IsEnabled DEFAULT (1),
        LastTestedAtUtc datetimeoffset(7) NULL,
        LastTestStatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
        LastTestMessage nvarchar(512) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_ai_model_config_Version DEFAULT (1),
        CONSTRAINT PK_fn_ai_model_config PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ai_model_config_ProviderKey
            CHECK (ProviderKey IN (N'openai_compatible', N'ollama')),
        CONSTRAINT CK_fn_ai_model_config_LastTestStatusKey
            CHECK (LastTestStatusKey IS NULL OR LastTestStatusKey IN (N'succeeded', N'failed'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能模型配置表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'ApiKeyProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的 API 密钥', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'ApiKeyProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'EndpointBaseUrl', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接口基础地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'EndpointBaseUrl';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'IsDefault', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否默认', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'IsDefault';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'LastTestMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次探测消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'LastTestMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'LastTestStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次探测状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'LastTestStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'LastTestedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last Tested At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'LastTestedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'ModelId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模型标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'ModelId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'OrganizationId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组织标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'OrganizationId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_config'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_config', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_ai_tenant_quota', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_tenant_quota
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        MonthlyTokenLimit bigint NULL,
        MonthlyRequestLimit bigint NULL,
        UsedTokensThisMonth bigint NOT NULL
            CONSTRAINT DF_fn_ai_tenant_quota_UsedTokensThisMonth DEFAULT (0),
        UsedRequestsThisMonth bigint NOT NULL
            CONSTRAINT DF_fn_ai_tenant_quota_UsedRequestsThisMonth DEFAULT (0),
        QuotaMonthKey varchar(7) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_ai_tenant_quota_IsEnabled DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_ai_tenant_quota_Version DEFAULT (1),
        CONSTRAINT PK_fn_ai_tenant_quota PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ai_tenant_quota_MonthlyTokenLimit
            CHECK (MonthlyTokenLimit IS NULL OR MonthlyTokenLimit >= 0),
        CONSTRAINT CK_fn_ai_tenant_quota_MonthlyRequestLimit
            CHECK (MonthlyRequestLimit IS NULL OR MonthlyRequestLimit >= 0),
        CONSTRAINT CK_fn_ai_tenant_quota_UsedTokensThisMonth
            CHECK (UsedTokensThisMonth >= 0),
        CONSTRAINT CK_fn_ai_tenant_quota_UsedRequestsThisMonth
            CHECK (UsedRequestsThisMonth >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能租户配额表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'MonthlyRequestLimit', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每月请求上限', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'MonthlyRequestLimit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'MonthlyTokenLimit', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每月 Token 上限', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'MonthlyTokenLimit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'QuotaMonthKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额月份键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'QuotaMonthKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'UsedRequestsThisMonth', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本月已用请求数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'UsedRequestsThisMonth';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'UsedTokensThisMonth', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本月已用 Token 数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'UsedTokensThisMonth';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_tenant_quota'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_tenant_quota', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ai_model_config')
      AND name = N'IX_fn_ai_model_config_IsEnabled_Name')
    CREATE INDEX IX_fn_ai_model_config_IsEnabled_Name
        ON dbo.fn_ai_model_config(IsEnabled, Name, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ai_model_config')
      AND name = N'IX_fn_ai_model_config_TenantId')
    CREATE INDEX IX_fn_ai_model_config_TenantId
        ON dbo.fn_ai_model_config(TenantId, Name, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
      AND name = N'UX_fn_ai_tenant_quota_TenantId')
    CREATE UNIQUE INDEX UX_fn_ai_tenant_quota_TenantId
        ON dbo.fn_ai_tenant_quota(TenantId);
