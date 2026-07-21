CREATE TABLE IF NOT EXISTS fn_organization_unit
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NOT NULL,
    ParentId BINARY(16) NULL,
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Name varchar(128) NOT NULL,
    DisplayOrder int NOT NULL,
    IsActive boolean NOT NULL DEFAULT true,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_organization_unit PRIMARY KEY (Id),
    CONSTRAINT FK_fn_organization_unit_Parent
        FOREIGN KEY (ParentId) REFERENCES fn_organization_unit(Id),
    UNIQUE KEY UX_fn_organization_unit_Tenant_Code (TenantId, Code),
    KEY IX_fn_organization_unit_Tenant_Parent (TenantId, ParentId, DisplayOrder)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
