-- 224: entitlement catalog, tenant bindings and enforcement phase.
CREATE TABLE IF NOT EXISTS fn_tenancy_entitlement_catalog (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '权益编码',
    Name varchar(128) NOT NULL COMMENT '权益名称',
    Description varchar(512) NULL COMMENT '描述',
    EntitlementType varchar(32) NOT NULL COMMENT '权益类型',
    IsActive boolean NOT NULL DEFAULT true COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_tenancy_entitlement_catalog PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_entitlement_catalog_Code (Code)
) COMMENT='租户权益目录表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_tenancy_tenant_entitlement_binding (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识',
    EntitlementId BINARY(16) NOT NULL COMMENT '权益标识',
    EffectiveFromUtc datetime(6) NOT NULL COMMENT '生效时间(UTC)',
    EffectiveToUtc datetime(6) NULL COMMENT '失效时间(UTC)',
    SourcePackageId BINARY(16) NULL COMMENT '来源套餐标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_tenancy_tenant_entitlement_binding PRIMARY KEY (Id),
    KEY IX_fn_tenancy_tenant_entitlement_binding_Tenant (TenantId, EntitlementId)
) COMMENT='租户权益绑定表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_tenancy_settings (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    EntitlementEnforcementPhase varchar(32) NOT NULL DEFAULT 'Compatibility' COMMENT '权益强制执行阶段',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_tenancy_settings PRIMARY KEY (Id)
) COMMENT='租户运行时设置表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO fn_tenancy_settings (Id, EntitlementEnforcementPhase, UpdatedAtUtc, Version)
VALUES (UUID_TO_BIN('00000000-0000-4000-8000-000000000010'), 'Compatibility', UTC_TIMESTAMP(6), 1);