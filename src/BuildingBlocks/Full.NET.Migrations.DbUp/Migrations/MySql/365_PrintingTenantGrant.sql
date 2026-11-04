-- 246：Host 按不可变发布版本显式授权租户；原子建表可重放，不自动开放存量打印模板。
CREATE TABLE IF NOT EXISTS fn_printing_template_tenant_grant (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '获授租户标识，不建立跨模块外键',
    TemplateId BINARY(16) NOT NULL COMMENT '本模块打印模板定义标识',
    VersionNumber int NOT NULL COMMENT '明确获授的不可变发布版本',
    GrantedByUserId BINARY(16) NOT NULL COMMENT '授权操作人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '授权时间(UTC)',
    CONSTRAINT PK_fn_printing_template_tenant_grant PRIMARY KEY (Id),
    CONSTRAINT UX_fn_printing_grant_Tenant_Template_Version UNIQUE (TenantId, TemplateId, VersionNumber)
) COMMENT='打印模板发布版本租户授权' ENGINE=InnoDB;
