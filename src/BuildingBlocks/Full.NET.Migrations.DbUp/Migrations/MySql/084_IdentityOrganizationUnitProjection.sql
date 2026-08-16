CREATE TABLE IF NOT EXISTS fn_identity_organization_unit_projection (
    TenantId BINARY(16) NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    UnitId BINARY(16) NOT NULL COMMENT '机构单元标识',
    Name varchar(128) NOT NULL COMMENT '名称',
    IsActive boolean NOT NULL COMMENT '是否启用',
    SourceVersion bigint NOT NULL COMMENT '源版本号',
    SourceUpdatedAtUtc datetime(6) NOT NULL COMMENT '源更新时间(UTC)',
    ProjectedAtUtc datetime(6) NOT NULL COMMENT '投影刷新时间(UTC)',
    PRIMARY KEY (TenantId, UnitId)
) COMMENT='身份认证机构单元投影表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
