-- 216：Identity OIDC 协议状态表；expand-only，支持幂等重跑。
CREATE TABLE IF NOT EXISTS fn_identity_oidc_application (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    ClientId varchar(256) NOT NULL COMMENT '客户端标识',
    ClientSecret longtext NULL COMMENT '客户端密钥',
    ConsentType varchar(64) NULL COMMENT '同意类型',
    DisplayName varchar(256) NULL COMMENT '显示名称',
    DisplayNamesJson longtext NULL COMMENT '本地化显示名称 JSON',
    PermissionsJson longtext NULL COMMENT '权限 JSON',
    PostLogoutRedirectUrisJson longtext NULL COMMENT '退出回调 JSON',
    PropertiesJson longtext NULL COMMENT '附加属性 JSON',
    RedirectUrisJson longtext NULL COMMENT '回调地址 JSON',
    RequirementsJson longtext NULL COMMENT '要求 JSON',
    ApplicationType varchar(64) NULL COMMENT '应用类型',
    JsonWebKeySetJson longtext NULL COMMENT 'JWKS JSON',
    SettingsJson longtext NULL COMMENT '设置 JSON',
    ClientType varchar(64) NULL COMMENT '客户端类型',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
    CONSTRAINT PK_fn_identity_oidc_application PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_oidc_application_ClientId (ClientId)
) COMMENT='Identity OIDC 客户端应用表';

CREATE TABLE IF NOT EXISTS fn_identity_oidc_authorization (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    ApplicationId binary(16) NULL COMMENT '所属客户端',
    CreationDateUtc datetime(6) NULL COMMENT '授权创建 UTC',
    PropertiesJson longtext NULL COMMENT '附加属性 JSON',
    ScopesJson longtext NULL COMMENT '作用域 JSON',
    Status varchar(64) NULL COMMENT '状态',
    Subject varchar(512) NULL COMMENT '主体',
    Type varchar(128) NULL COMMENT '授权类型',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
    CONSTRAINT PK_fn_identity_oidc_authorization PRIMARY KEY (Id),
    KEY IX_fn_identity_oidc_authorization_ApplicationId (ApplicationId),
    CONSTRAINT FK_fn_identity_oidc_authorization_Application
        FOREIGN KEY (ApplicationId) REFERENCES fn_identity_oidc_application (Id)
) COMMENT='Identity OIDC 授权表';

CREATE TABLE IF NOT EXISTS fn_identity_oidc_scope (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    Name varchar(256) NOT NULL COMMENT '作用域名称',
    Description varchar(512) NULL COMMENT '描述',
    DescriptionsJson longtext NULL COMMENT '本地化描述 JSON',
    DisplayName varchar(256) NULL COMMENT '显示名称',
    DisplayNamesJson longtext NULL COMMENT '本地化显示名称 JSON',
    PropertiesJson longtext NULL COMMENT '附加属性 JSON',
    ResourcesJson longtext NULL COMMENT '资源 JSON',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
    CONSTRAINT PK_fn_identity_oidc_scope PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_oidc_scope_Name (Name)
) COMMENT='Identity OIDC 作用域表';

CREATE TABLE IF NOT EXISTS fn_identity_oidc_token (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    ApplicationId binary(16) NULL COMMENT '所属客户端',
    AuthorizationId binary(16) NULL COMMENT '所属授权',
    CreationDateUtc datetime(6) NULL COMMENT '创建 UTC',
    ExpirationDateUtc datetime(6) NULL COMMENT '过期 UTC',
    Payload longtext NULL COMMENT '载荷',
    PropertiesJson longtext NULL COMMENT '附加属性 JSON',
    RedemptionDateUtc datetime(6) NULL COMMENT '兑换 UTC',
    ReferenceId varchar(256) NULL COMMENT '引用标识',
    Status varchar(64) NULL COMMENT '状态',
    Subject varchar(512) NULL COMMENT '主体',
    Type varchar(256) NULL COMMENT '令牌类型',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
    CONSTRAINT PK_fn_identity_oidc_token PRIMARY KEY (Id),
    KEY IX_fn_identity_oidc_token_ApplicationId (ApplicationId),
    KEY IX_fn_identity_oidc_token_AuthorizationId (AuthorizationId),
    UNIQUE KEY UX_fn_identity_oidc_token_ReferenceId (ReferenceId),
    CONSTRAINT FK_fn_identity_oidc_token_Application
        FOREIGN KEY (ApplicationId) REFERENCES fn_identity_oidc_application (Id),
    CONSTRAINT FK_fn_identity_oidc_token_Authorization
        FOREIGN KEY (AuthorizationId) REFERENCES fn_identity_oidc_authorization (Id)
) COMMENT='Identity OIDC 令牌表';