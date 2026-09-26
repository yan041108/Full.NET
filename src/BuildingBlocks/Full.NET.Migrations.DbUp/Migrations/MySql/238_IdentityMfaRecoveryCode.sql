-- MFA 恢复码摘要表：仅保存哈希，明文仅在生成时返回一次。
CREATE TABLE IF NOT EXISTS fn_identity_user_mfa_recovery_code
(
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    CodeHash BINARY(32) NOT NULL COMMENT '恢复码哈希',
    ConsumedAtUtc DATETIME(6) NULL COMMENT '消费时间(UTC)',
    CreatedAtUtc DATETIME(6) NOT NULL COMMENT '创建时间(UTC)',
    Version INT NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT FK_fn_identity_user_mfa_recovery_code_UserId
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id)
) COMMENT='身份认证 MFA 恢复码表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE INDEX IX_fn_identity_user_mfa_recovery_code_UserId
    ON fn_identity_user_mfa_recovery_code(UserId);
