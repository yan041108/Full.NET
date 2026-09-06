-- 151：OAuth/OIDC 提供程序、用户绑定与授权状态表。

CREATE TABLE IF NOT EXISTS fn_identity_oauth_provider (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ProviderKey varchar(64) NOT NULL COMMENT '稳定机器码',
    DisplayName varchar(128) NOT NULL COMMENT '显示名称',
    Authority varchar(512) NOT NULL COMMENT 'OIDC Issuer URL',
    ClientId varchar(256) NOT NULL COMMENT '客户端标识',
    ClientSecretProtected text NOT NULL COMMENT '受保护的客户端密钥',
    Scopes varchar(512) NOT NULL DEFAULT 'openid profile email' COMMENT '授权范围',
    RedirectPath varchar(256) NOT NULL DEFAULT '/api/v1/identity/oauth/callback' COMMENT '回调路径',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_oauth_provider PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_oauth_provider_ProviderKey (ProviderKey),
    KEY IX_fn_identity_oauth_provider_IsEnabled_DisplayName (IsEnabled, DisplayName, Id)
) COMMENT='OAuth/OIDC 身份提供程序配置表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_oauth_user_link (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    UserId BINARY(16) NOT NULL COMMENT '本地用户标识',
    ProviderKey varchar(64) NOT NULL COMMENT '提供程序机器码',
    Subject varchar(256) NOT NULL COMMENT '外部主体标识',
    Email varchar(256) NULL COMMENT '外部邮箱（仅元数据）',
    EmailVerified tinyint(1) NOT NULL DEFAULT 0 COMMENT '邮箱是否已验证',
    DisplayName varchar(256) NULL COMMENT '外部显示名称',
    LinkedAtUtc datetime(6) NOT NULL COMMENT '绑定时间(UTC)',
    LastUsedAtUtc datetime(6) NULL COMMENT '最近使用时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_oauth_user_link PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_oauth_user_link_ProviderKey_Subject (ProviderKey, Subject),
    UNIQUE KEY UX_fn_identity_oauth_user_link_UserId_ProviderKey (UserId, ProviderKey),
    CONSTRAINT FK_fn_identity_oauth_user_link_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user (Id)
) COMMENT='OAuth/OIDC 外部身份与本地用户绑定表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_oauth_authorization_state (
    Id BINARY(16) NOT NULL COMMENT '状态标识（同时作为 OAuth state）',
    ProviderKey varchar(64) NOT NULL COMMENT '提供程序机器码',
    CodeVerifier varchar(128) NOT NULL COMMENT 'PKCE code_verifier',
    Nonce varchar(128) NOT NULL COMMENT 'OIDC nonce',
    Mode varchar(16) NOT NULL COMMENT 'login 或 bind',
    UserId BINARY(16) NULL COMMENT 'bind 模式下的当前用户',
    ReturnUrl varchar(2048) NOT NULL COMMENT '完成后重定向地址',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期时间(UTC)',
    CONSTRAINT PK_fn_identity_oauth_authorization_state PRIMARY KEY (Id),
    KEY IX_fn_identity_oauth_authorization_state_ExpiresAtUtc (ExpiresAtUtc, Id)
) COMMENT='OAuth/OIDC 授权状态临时表（多实例安全）' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
