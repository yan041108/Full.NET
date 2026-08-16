CREATE TABLE IF NOT EXISTS fn_organization_unit (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    ParentId BINARY(16) NULL COMMENT '父级标识',
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '编码',
    Name varchar(128) NOT NULL COMMENT '名称',
    DisplayOrder int NOT NULL COMMENT '显示顺序',
    IsActive boolean NOT NULL DEFAULT true COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_organization_unit PRIMARY KEY (Id),
    CONSTRAINT FK_fn_organization_unit_Parent
        FOREIGN KEY (ParentId) REFERENCES fn_organization_unit(Id),
    UNIQUE KEY UX_fn_organization_unit_Tenant_Code (TenantId, Code),
    KEY IX_fn_organization_unit_Tenant_Parent (TenantId, ParentId, DisplayOrder)
) COMMENT='组织机构机构单元表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
