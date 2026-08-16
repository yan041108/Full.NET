-- 031：Host 作用域 API Key 凭据（仅保存哈希）。

CREATE TABLE IF NOT EXISTS fn_identity_api_key (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    DisplayName varchar(128) NOT NULL COMMENT '显示名称',
    KeyPrefix varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '密钥前缀',
    KeyHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '密钥哈希',
    PermissionsJson varchar(4000) NOT NULL COMMENT '权限集合(JSON)',
    ExpiresAtUtc datetime(6) NULL COMMENT '过期时间(UTC)',
    IsActive tinyint(1) NOT NULL COMMENT '是否启用',
    LastUsedAtUtc datetime(6) NULL COMMENT '最后使用时间(UTC)',
    DisabledAtUtc datetime(6) NULL COMMENT '禁用时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_api_key PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_api_key_KeyHash (KeyHash),
    KEY IX_fn_identity_api_key_UserCreatedAtUtc (UserId, CreatedAtUtc, Id)
) COMMENT='身份认证API 密钥表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
