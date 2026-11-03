-- Host 定义显式发布到租户；无跨模块外键，旧导出不补造原会话。
CREATE TABLE IF NOT EXISTS fn_reporting_definition_tenant_grant (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7',
    DefinitionId binary(16) NOT NULL COMMENT '本模块定义身份',
    TenantId binary(16) NOT NULL COMMENT 'Host 明确授权的目标租户，无跨模块外键',
    IsEnabled bit NOT NULL COMMENT '显式发布授权启用状态',
    Version int NOT NULL COMMENT '授权并发版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC 时间',
    CreatedByUserId binary(16) NOT NULL COMMENT '可信创建主体',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新 UTC 时间',
    UpdatedByUserId binary(16) NULL COMMENT '可信更新主体',
    CONSTRAINT PK_fn_reporting_definition_tenant_grant PRIMARY KEY (Id),
    CONSTRAINT UX_fn_reporting_def_grant_Tenant_Definition UNIQUE (TenantId,DefinitionId),
    INDEX IX_fn_reporting_def_grant_Definition (DefinitionId,TenantId)
) COMMENT='报表定义到租户的显式发布授权' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

SET @fullnet_reporting_binding_sql = IF(
    EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_reporting_export_task' AND COLUMN_NAME='ActorSessionBindingJson'),
    'SELECT 1',
    'ALTER TABLE fn_reporting_export_task ADD COLUMN ActorSessionBindingJson varchar(2048) NULL COMMENT ''私有原会话可信绑定；缺失时执行和下载失败关闭''');
PREPARE fullnet_reporting_binding_statement FROM @fullnet_reporting_binding_sql;
EXECUTE fullnet_reporting_binding_statement;
DEALLOCATE PREPARE fullnet_reporting_binding_statement;
