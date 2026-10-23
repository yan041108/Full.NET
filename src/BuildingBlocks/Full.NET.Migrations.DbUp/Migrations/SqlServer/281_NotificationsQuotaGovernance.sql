-- 280：Notifications 发送配额治理（Provider/租户/Producer/Scene）。
-- 政策表声明有界额度；计数器表按窗口原子预留，跨实例不超卖；预留台账支撑成功核销、
-- 确定未发送释放与 Unknown 暂挂对账。缺失配置时发送链路失败关闭，不允许无界并发。

IF OBJECT_ID(N'dbo.fn_notifications_quota_policy', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_quota_policy
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DimensionKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DimensionValue nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        WindowSeconds int NOT NULL,
        MaxCount int NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_fn_notifications_quota_policy_Enabled DEFAULT (1),
        Version bigint NOT NULL CONSTRAINT DF_fn_notifications_quota_policy_Version DEFAULT (1),
        CreatedById uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_quota_policy PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_notifications_quota_policy_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_notifications_quota_policy_Dimension CHECK (DimensionKey IN ('provider', 'tenant', 'producer', 'scene')),
        CONSTRAINT CK_fn_notifications_quota_policy_Window CHECK (WindowSeconds > 0),
        CONSTRAINT CK_fn_notifications_quota_policy_MaxCount CHECK (MaxCount > 0),
        CONSTRAINT CK_fn_notifications_quota_policy_Version CHECK (Version > 0),
        CONSTRAINT UX_fn_notifications_quota_policy_Scope UNIQUE (TenantScopeKey, DimensionKey, DimensionValue)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知发送配额政策表；子租户精确声明优先于 host 通配声明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'DimensionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额维度键：provider/tenant/producer/scene', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'DimensionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'DimensionValue', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额维度取值；* 表示该维度全部取值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'DimensionValue';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'MaxCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单个窗口内允许的发送上限（不含已释放的确定未发送）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'MaxCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'WindowSeconds', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定窗口长度（秒）；窗口起点按 Unix 时间对齐', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'WindowSeconds';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知发送配额政策表；子租户精确声明优先于 host 通配声明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'DimensionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额维度键：provider/tenant/producer/scene', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'DimensionKey';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'DimensionValue', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额维度取值；* 表示该维度全部取值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'DimensionValue';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'WindowSeconds', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定窗口长度（秒）；窗口起点按 Unix 时间对齐', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'WindowSeconds';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'MaxCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单个窗口内允许的发送上限（不含已释放的确定未发送）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'MaxCount';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_policy'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'政策是否启用；停用的政策不再参与配额解析', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_policy', @level2type=N'COLUMN', @level2name=N'IsEnabled';
END;

IF OBJECT_ID(N'dbo.fn_notifications_quota_counter', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_quota_counter
    (
        Id uniqueidentifier NOT NULL,
        PolicyId uniqueidentifier NOT NULL,
        WindowStartUtc datetime2(6) NOT NULL,
        WindowSeconds int NOT NULL,
        ReservedCount int NOT NULL CONSTRAINT DF_fn_notifications_quota_counter_Reserved DEFAULT (0),
        ConsumedCount int NOT NULL CONSTRAINT DF_fn_notifications_quota_counter_Consumed DEFAULT (0),
        ReleasedCount int NOT NULL CONSTRAINT DF_fn_notifications_quota_counter_Released DEFAULT (0),
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_quota_counter PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_quota_counter_Policy FOREIGN KEY (PolicyId) REFERENCES dbo.fn_notifications_quota_policy(Id),
        CONSTRAINT UX_fn_notifications_quota_counter_Window UNIQUE (PolicyId, WindowStartUtc),
        CONSTRAINT CK_fn_notifications_quota_counter_Counts CHECK (ReservedCount >= 0 AND ConsumedCount >= 0 AND ReleasedCount >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知发送配额窗口计数器；占用次数为累计预留减去确定释放', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'ConsumedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'成功核销次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'ConsumedCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'PolicyId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额政策标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'PolicyId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'ReleasedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确定未发送而释放的次数；释放后额度回到窗口', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'ReleasedCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'ReservedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'累计预留次数；含已核销与暂挂对账', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'ReservedCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'WindowSeconds', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'窗口长度（秒）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'WindowSeconds';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'WindowStartUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'窗口起点(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'WindowStartUtc';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知发送配额窗口计数器；在飞占用=ReservedCount-ReleasedCount', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'ReservedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'累计预留次数；含已核销与暂挂对账', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'ReservedCount';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_counter')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_counter'), N'ReleasedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确定未发送而释放的次数；释放后额度回到窗口', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_counter', @level2type=N'COLUMN', @level2name=N'ReleasedCount';
END;

IF OBJECT_ID(N'dbo.fn_notifications_quota_reservation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_quota_reservation
    (
        Id uniqueidentifier NOT NULL,
        DeliveryId uniqueidentifier NOT NULL,
        PolicyId uniqueidentifier NOT NULL,
        DimensionKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        WindowStartUtc datetime2(6) NOT NULL,
        LeaseGeneration bigint NOT NULL,
        StateKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_quota_reservation PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_quota_reservation_Delivery FOREIGN KEY (DeliveryId) REFERENCES dbo.fn_notifications_delivery(Id),
        CONSTRAINT FK_fn_notifications_quota_reservation_Policy FOREIGN KEY (PolicyId) REFERENCES dbo.fn_notifications_quota_policy(Id),
        CONSTRAINT CK_fn_notifications_quota_reservation_State CHECK (StateKey IN ('reserved', 'consumed', 'released', 'pending_reconciliation')),
        CONSTRAINT CK_fn_notifications_quota_reservation_LeaseGen CHECK (LeaseGeneration > 0),
        CONSTRAINT UX_fn_notifications_quota_reservation_Attempt UNIQUE (DeliveryId, PolicyId, LeaseGeneration)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知发送配额预留台账；一次领取一批预留，按结果核销、释放或暂挂对账', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'DeliveryId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'投递标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'DeliveryId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'DimensionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额维度键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'DimensionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'LeaseGeneration', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留所属领取世代；同一次领取只占额一次', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'LeaseGeneration';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'PolicyId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额政策标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'PolicyId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'StateKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留状态：reserved/consumed/released/pending_reconciliation', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'StateKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'WindowStartUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留所属窗口起点(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'WindowStartUtc';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知发送配额预留台账；一次领取一批预留，按结果核销、释放或暂挂对账', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_quota_reservation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_quota_reservation'), N'StateKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留状态：reserved/consumed/released/pending_reconciliation', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_quota_reservation', @level2type=N'COLUMN', @level2name=N'StateKey';
END;
