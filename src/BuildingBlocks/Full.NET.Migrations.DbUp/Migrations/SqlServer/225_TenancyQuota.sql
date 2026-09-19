-- 建立租户配额度量及幂等预留账本；SQL Server 使用 uniqueidentifier、显式非聚集主键与扩展属性注释。
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

-- 注释独立于建表分支，支持表已创建但迁移尚未记账时补齐。
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户配额度量表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'LimitValue', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'上限', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'LimitValue';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'MetricCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'度量编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'MetricCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'PeriodKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'周期键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'PeriodKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'ReservedValue', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'ReservedValue';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'UsedValue', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已用量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'UsedValue';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_metric')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_metric'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_metric', @level2type=N'COLUMN', @level2name=N'Version';

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

-- 注释独立于建表分支，支持表已创建但迁移尚未记账时补齐。
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户配额预留表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'Amount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'Amount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'MetricCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'度量编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'MetricCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'OperationId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等操作标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'OperationId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation', @level2type=N'COLUMN', @level2name=N'Version';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_tenancy_quota_reservation_Operation' AND object_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_tenancy_quota_reservation_Operation
        ON dbo.fn_tenancy_quota_reservation (TenantId, MetricCode, OperationId);
