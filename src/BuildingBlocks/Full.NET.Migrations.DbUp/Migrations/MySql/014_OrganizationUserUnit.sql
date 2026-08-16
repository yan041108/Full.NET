CREATE TABLE IF NOT EXISTS fn_organization_user_unit (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    UnitId BINARY(16) NOT NULL COMMENT '机构单元标识',
    IsPrimary tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否主关联',
    IsActive tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_organization_user_unit PRIMARY KEY (Id),
    CONSTRAINT FK_fn_organization_user_unit_Unit
        FOREIGN KEY (UnitId) REFERENCES fn_organization_unit(Id),
    UNIQUE KEY UX_fn_organization_user_unit_Tenant_User_Unit (TenantId, UserId, UnitId),
    KEY IX_fn_organization_user_unit_Tenant_User (TenantId, UserId, IsPrimary),
    KEY IX_fn_organization_user_unit_Tenant_Unit (TenantId, UnitId)
) COMMENT='组织机构用户机构表' ENGINE=InnoDB;
