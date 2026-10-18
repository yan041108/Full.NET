-- 245：Host 按不可变发布版本显式授权租户；原子建表可重放，不自动开放存量报表。
CREATE TABLE IF NOT EXISTS fn_reporting_definition_tenant_grant (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '获授租户标识，不建立跨模块外键',
    DefinitionId BINARY(16) NOT NULL COMMENT '本模块报表定义标识',
    VersionNumber int NOT NULL COMMENT '明确获授的不可变发布版本',
    GrantedByUserId BINARY(16) NOT NULL COMMENT '授权操作人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '授权时间(UTC)',
    CONSTRAINT PK_fn_reporting_definition_tenant_grant PRIMARY KEY (Id),
    CONSTRAINT UX_fn_reporting_grant_Tenant_Definition_Version UNIQUE (TenantId, DefinitionId, VersionNumber)
) COMMENT='报表发布版本租户授权' ENGINE=InnoDB;
