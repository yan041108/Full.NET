-- 149：LDAP 连接配置主表。

CREATE TABLE IF NOT EXISTS fn_identity_ldap_connection (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '所属租户标识；为空表示 Host 级配置',
    Name varchar(128) NOT NULL COMMENT '显示名称',
    Host varchar(256) NOT NULL COMMENT 'LDAP 主机名',
    Port int NOT NULL DEFAULT 389 COMMENT 'LDAP 端口',
    UseTls tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否启用 TLS',
    BaseDn varchar(512) NOT NULL COMMENT '目录根 DN',
    BindDn varchar(512) NOT NULL COMMENT '服务账号绑定 DN',
    BindPasswordProtected text NOT NULL COMMENT '受保护的服务账号密码',
    UserSearchFilter varchar(256) NOT NULL DEFAULT '(sAMAccountName={0})' COMMENT '用户搜索过滤器模板',
    UserAccountAttribute varchar(128) NOT NULL DEFAULT 'sAMAccountName' COMMENT '用户账号属性名',
    EmployeeIdAttribute varchar(128) NULL COMMENT '工号属性名',
    DepartmentCodeAttribute varchar(128) NULL COMMENT '部门编码属性名',
    SyncSearchBaseDn varchar(512) NOT NULL COMMENT '同步预览允许的搜索根 DN',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_ldap_connection PRIMARY KEY (Id),
    KEY IX_fn_identity_ldap_connection_IsEnabled_Name (IsEnabled, Name, Id)
) COMMENT='LDAP 目录连接配置表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
