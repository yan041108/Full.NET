-- 018：Host 租户套餐目录主数据；套餐与租户绑定在后续切片交付。
CREATE TABLE IF NOT EXISTS fn_tenancy_tenant_package
(
    Id BINARY(16) NOT NULL,
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Name varchar(128) NOT NULL,
    Description varchar(512) NULL,
    IsActive boolean NOT NULL DEFAULT true,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_tenancy_tenant_package PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_tenant_package_Code (Code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
