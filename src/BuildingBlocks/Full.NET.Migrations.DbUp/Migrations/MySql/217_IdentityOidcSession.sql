-- 217：Identity OIDC 中心会话与应用会话表；expand-only，支持幂等重跑。
CREATE TABLE IF NOT EXISTS fn_identity_oidc_center_session (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    UserId binary(16) NOT NULL COMMENT '用户标识',
    SecurityStamp varchar(64) NOT NULL COMMENT '创建时安全戳快照',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期 UTC',
    RevokedAtUtc datetime(6) NULL COMMENT '撤销 UTC',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观版本',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
    CONSTRAINT PK_fn_identity_oidc_center_session PRIMARY KEY (Id),
    KEY IX_fn_identity_oidc_center_session_UserId (UserId)
) COMMENT='Identity OIDC 中心会话表';

CREATE TABLE IF NOT EXISTS fn_identity_oidc_application_session (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    CenterSessionId binary(16) NOT NULL COMMENT '中心会话标识',
    OidcApplicationId binary(16) NOT NULL COMMENT 'OIDC 客户端应用标识',
    ClientId varchar(256) NOT NULL COMMENT 'OAuth client_id',
    UserId binary(16) NOT NULL COMMENT '用户标识',
    ActorScope varchar(64) NOT NULL COMMENT '演员原始作用域',
    EffectiveScope varchar(128) NOT NULL COMMENT '当前有效作用域',
    ActiveTenantId binary(16) NULL COMMENT '活动租户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期 UTC',
    RevokedAtUtc datetime(6) NULL COMMENT '撤销 UTC',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观版本',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
    CONSTRAINT PK_fn_identity_oidc_application_session PRIMARY KEY (Id),
    KEY IX_fn_identity_oidc_application_session_CenterSessionId (CenterSessionId),
    KEY IX_fn_identity_oidc_application_session_UserId (UserId),
    KEY IX_fn_identity_oidc_application_session_UserId_ClientId (UserId, ClientId),
    CONSTRAINT FK_fn_identity_oidc_application_session_Center
        FOREIGN KEY (CenterSessionId) REFERENCES fn_identity_oidc_center_session (Id),
    CONSTRAINT FK_fn_identity_oidc_application_session_Application
        FOREIGN KEY (OidcApplicationId) REFERENCES fn_identity_oidc_application (Id)
) COMMENT='Identity OIDC 应用会话表';