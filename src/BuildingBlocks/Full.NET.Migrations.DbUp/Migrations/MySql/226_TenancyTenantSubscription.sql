-- 226: tenant subscription lifecycle records.
CREATE TABLE IF NOT EXISTS fn_tenancy_tenant_subscription (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识',
    PackageId BINARY(16) NULL COMMENT '套餐标识',
    Status varchar(32) NOT NULL COMMENT '订阅状态',
    TrialEndsAtUtc datetime(6) NULL COMMENT '试用结束(UTC)',
    CurrentPeriodStartUtc datetime(6) NOT NULL COMMENT '当前周期开始(UTC)',
    CurrentPeriodEndUtc datetime(6) NOT NULL COMMENT '当前周期结束(UTC)',
    CancelledAtUtc datetime(6) NULL COMMENT '取消时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    ActiveSubscriptionKey varchar(8) GENERATED ALWAYS AS (
        CASE WHEN Status IN ('Trial', 'Active', 'PastDue') THEN 'active' ELSE NULL END
    ) STORED COMMENT '活跃订阅唯一键辅助列',
    CONSTRAINT PK_fn_tenancy_tenant_subscription PRIMARY KEY (Id),
    KEY IX_fn_tenancy_tenant_subscription_TenantId (TenantId),
    UNIQUE KEY UX_fn_tenancy_tenant_subscription_TenantActive (TenantId, ActiveSubscriptionKey)
) COMMENT='租户订阅表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
