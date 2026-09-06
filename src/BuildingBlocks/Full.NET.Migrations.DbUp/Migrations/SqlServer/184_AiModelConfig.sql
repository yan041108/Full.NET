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
