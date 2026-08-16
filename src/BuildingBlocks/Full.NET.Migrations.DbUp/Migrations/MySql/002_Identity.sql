CREATE TABLE IF NOT EXISTS fn_identity_user (
    Id char(36) NOT NULL PRIMARY KEY COMMENT '逻辑主键',
    TenantId char(36) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(64) NOT NULL COMMENT '作用域键',
    Username varchar(128) NOT NULL COMMENT '用户名',
    NormalizedUsername varchar(128) NOT NULL COMMENT '规范化用户名',
    DisplayName varchar(128) NOT NULL COMMENT '显示名称',
    PasswordHash varchar(1024) NOT NULL COMMENT '密码哈希',
    IsActive boolean NOT NULL COMMENT '是否启用',
    FailedLoginCount int NOT NULL DEFAULT 0 COMMENT '登录失败次数',
    LockoutEndUtc datetime(6) NULL COMMENT '锁定结束时间(UTC)',
    SecurityStamp varchar(64) NOT NULL COMMENT '安全戳',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    UNIQUE KEY UX_fn_identity_user_Scope_Username (ScopeKey, NormalizedUsername)
) COMMENT='身份认证用户表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_refresh_session (
    Id char(36) NOT NULL PRIMARY KEY COMMENT '逻辑主键',
    UserId char(36) NOT NULL COMMENT '用户标识',
    FamilyId char(36) NOT NULL COMMENT '会话族标识',
    ClientId varchar(64) NOT NULL COMMENT '客户端标识',
    TokenHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '令牌哈希',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期时间(UTC)',
    ConsumedAtUtc datetime(6) NULL COMMENT '消费时间(UTC)',
    RevokedAtUtc datetime(6) NULL COMMENT '撤销时间(UTC)',
    ReplacedById char(36) NULL COMMENT '替换会话标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT FK_fn_identity_refresh_session_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id),
    UNIQUE KEY UX_fn_identity_refresh_session_TokenHash (TokenHash),
    KEY IX_fn_identity_refresh_session_Family (FamilyId, RevokedAtUtc, ExpiresAtUtc),
    KEY IX_fn_identity_refresh_session_User (UserId, RevokedAtUtc, ExpiresAtUtc)
) COMMENT='用户刷新令牌会话' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_auth_audit (
    Id char(36) NOT NULL PRIMARY KEY COMMENT '逻辑主键',
    UserId char(36) NULL COMMENT '用户标识',
    SessionId char(36) NULL COMMENT '会话标识',
    UsernameFingerprint char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '用户名指纹',
    EventType varchar(64) NOT NULL COMMENT '事件类型',
    ResultCode varchar(128) NOT NULL COMMENT '结果码',
    Succeeded boolean NOT NULL COMMENT '是否成功',
    IpAddress varchar(64) NULL COMMENT 'IP 地址',
    UserAgent varchar(512) NULL COMMENT '用户代理',
    OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时间(UTC)',
    CONSTRAINT FK_fn_identity_auth_audit_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id),
    KEY IX_fn_identity_auth_audit_OccurredAt (OccurredAtUtc),
    KEY IX_fn_identity_auth_audit_User (UserId, OccurredAtUtc)
) COMMENT='身份认证审计事件' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
