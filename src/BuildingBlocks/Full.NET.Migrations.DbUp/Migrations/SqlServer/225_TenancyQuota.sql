-- 225: unified quota metrics and reservations.
IF OBJECT_ID(N'dbo.fn_tenancy_quota_metric', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_quota_metric (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        MetricCode nvarchar(64) NOT NULL,
        PeriodKey nvarchar(32) NOT NULL,
        LimitValue bigint NOT NULL,
        UsedValue bigint NOT NULL CONSTRAINT DF_fn_tenancy_quota_metric_UsedValue DEFAULT (0),
        ReservedValue bigint NOT NULL CONSTRAINT DF_fn_tenancy_quota_metric_ReservedValue DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenancy_quota_metric_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_quota_metric PRIMARY KEY NONCLUSTERED (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_tenancy_quota_metric_TenantMetricPeriod' AND object_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_tenancy_quota_metric_TenantMetricPeriod
        ON dbo.fn_tenancy_quota_metric (TenantId, MetricCode, PeriodKey);

IF OBJECT_ID(N'dbo.fn_tenancy_quota_reservation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_quota_reservation (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        MetricCode nvarchar(64) NOT NULL,
        OperationId nvarchar(128) NOT NULL,
        Amount bigint NOT NULL,
        Status nvarchar(32) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenancy_quota_reservation_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_quota_reservation PRIMARY KEY NONCLUSTERED (Id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_tenancy_quota_reservation_Operation' AND object_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_tenancy_quota_reservation_Operation
        ON dbo.fn_tenancy_quota_reservation (TenantId, MetricCode, OperationId);
