CREATE TABLE IF NOT EXISTS fn_organization_user_position (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    PositionId BINARY(16) NOT NULL COMMENT '岗位标识',
    IsPrimary tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否主关联',
    IsActive tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_organization_user_position PRIMARY KEY (Id),
    CONSTRAINT FK_fn_organization_user_position_Position
        FOREIGN KEY (PositionId) REFERENCES fn_organization_position(Id),
    UNIQUE KEY UX_fn_organization_user_position_Tenant_User_Position (TenantId, UserId, PositionId),
    KEY IX_fn_organization_user_position_Tenant_User (TenantId, UserId, IsPrimary),
    KEY IX_fn_organization_user_position_Tenant_Position (TenantId, PositionId)
) COMMENT='组织机构用户岗位表' ENGINE=InnoDB;
