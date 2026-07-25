-- 025：租户职位目录。
CREATE TABLE IF NOT EXISTS fn_organization_position
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NOT NULL,
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Name varchar(128) NOT NULL,
    DisplayOrder int NOT NULL,
    IsActive boolean NOT NULL DEFAULT true,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_organization_position PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_organization_position_Tenant_Code (TenantId, Code),
    KEY IX_fn_organization_position_Tenant_DisplayOrder (TenantId, DisplayOrder, Code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
