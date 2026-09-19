-- 225: unified quota metrics and reservations.
CREATE TABLE IF NOT EXISTS fn_tenancy_quota_metric (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识',
    MetricCode varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '度量编码',
    PeriodKey varchar(32) NOT NULL COMMENT '周期键',
    LimitValue bigint NOT NULL COMMENT '上限',
    UsedValue bigint NOT NULL DEFAULT 0 COMMENT '已用量',
    ReservedValue bigint NOT NULL DEFAULT 0 COMMENT '预留量',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_tenancy_quota_metric PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_quota_metric_TenantMetricPeriod (TenantId, MetricCode, PeriodKey)
) COMMENT='租户配额度量表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_tenancy_quota_reservation (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识',
    MetricCode varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '度量编码',
    OperationId varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '幂等操作标识',
    Amount bigint NOT NULL COMMENT '预留数量',
    Status varchar(32) NOT NULL COMMENT '预留状态',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_tenancy_quota_reservation PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_quota_reservation_Operation (TenantId, MetricCode, OperationId)
) COMMENT='租户配额预留表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;