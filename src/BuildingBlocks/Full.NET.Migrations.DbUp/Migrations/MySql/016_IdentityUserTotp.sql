-- 为 Host 账号持久化 TOTP 强认证密钥（Data Protection 密文）。
-- MySQL：UserId 使用 BINARY(16) 与 fn_identity_user.Id 对齐。

CREATE TABLE IF NOT EXISTS fn_identity_user_totp (
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    SecretProtected varchar(512) NOT NULL COMMENT '受保护密钥',
    IsEnabled tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否启用',
    ConfirmedAtUtc datetime(6) NULL COMMENT '确认时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_user_totp PRIMARY KEY (UserId),
    CONSTRAINT FK_fn_identity_user_totp_UserId
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id)
) COMMENT='身份认证用户 TOTP表' ENGINE=InnoDB;
