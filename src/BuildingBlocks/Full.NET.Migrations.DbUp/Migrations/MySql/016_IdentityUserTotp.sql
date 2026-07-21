-- 为 Host 账号持久化 TOTP 强认证密钥（Data Protection 密文）。
-- MySQL：UserId 使用 BINARY(16) 与 fn_identity_user.Id 对齐。

CREATE TABLE IF NOT EXISTS fn_identity_user_totp
(
    UserId BINARY(16) NOT NULL,
    SecretProtected varchar(512) NOT NULL,
    IsEnabled tinyint(1) NOT NULL DEFAULT 0,
    ConfirmedAtUtc datetime(6) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_identity_user_totp PRIMARY KEY (UserId),
    CONSTRAINT FK_fn_identity_user_totp_UserId
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id)
) ENGINE=InnoDB;
