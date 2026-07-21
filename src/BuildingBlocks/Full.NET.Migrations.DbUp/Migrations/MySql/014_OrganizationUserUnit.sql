CREATE TABLE IF NOT EXISTS fn_organization_user_unit
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NOT NULL,
    UserId BINARY(16) NOT NULL,
    UnitId BINARY(16) NOT NULL,
    IsPrimary tinyint(1) NOT NULL DEFAULT 0,
    IsActive tinyint(1) NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_organization_user_unit PRIMARY KEY (Id),
    CONSTRAINT FK_fn_organization_user_unit_Unit
        FOREIGN KEY (UnitId) REFERENCES fn_organization_unit(Id),
    UNIQUE KEY UX_fn_organization_user_unit_Tenant_User_Unit (TenantId, UserId, UnitId),
    KEY IX_fn_organization_user_unit_Tenant_User (TenantId, UserId, IsPrimary),
    KEY IX_fn_organization_user_unit_Tenant_Unit (TenantId, UnitId)
) ENGINE=InnoDB;
