CREATE TABLE IF NOT EXISTS fn_organization_user_position
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NOT NULL,
    UserId BINARY(16) NOT NULL,
    PositionId BINARY(16) NOT NULL,
    IsPrimary tinyint(1) NOT NULL DEFAULT 0,
    IsActive tinyint(1) NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_organization_user_position PRIMARY KEY (Id),
    CONSTRAINT FK_fn_organization_user_position_Position
        FOREIGN KEY (PositionId) REFERENCES fn_organization_position(Id),
    UNIQUE KEY UX_fn_organization_user_position_Tenant_User_Position (TenantId, UserId, PositionId),
    KEY IX_fn_organization_user_position_Tenant_User (TenantId, UserId, IsPrimary),
    KEY IX_fn_organization_user_position_Tenant_Position (TenantId, PositionId)
) ENGINE=InnoDB;
