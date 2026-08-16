-- 018：Host 租户套餐目录主数据；套餐与租户绑定在后续切片交付。
CREATE TABLE IF NOT EXISTS fn_tenancy_tenant_package (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '编码',
    Name varchar(128) NOT NULL COMMENT '名称',
    Description varchar(512) NULL COMMENT '描述',
    IsActive boolean NOT NULL DEFAULT true COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_tenancy_tenant_package PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_tenant_package_Code (Code)
) COMMENT='租户租户套餐表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
