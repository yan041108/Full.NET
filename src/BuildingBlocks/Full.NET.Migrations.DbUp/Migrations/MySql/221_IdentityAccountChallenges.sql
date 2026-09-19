-- 221：Identity 账号操作挑战。

CREATE TABLE IF NOT EXISTS fn_identity_account_challenge (
    ChallengeId BINARY(16) NOT NULL COMMENT '逻辑主键',
    Purpose tinyint NOT NULL COMMENT '挑战用途',
    NormalizedEmail varchar(320) NOT NULL COMMENT '规范化邮箱',
    CredentialHash char(64) NOT NULL COMMENT '凭据摘要',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期时间(UTC)',
    ConsumedAtUtc datetime(6) NULL COMMENT '消费时间(UTC)',
    AttemptCount int NOT NULL DEFAULT 0 COMMENT '尝试次数',
    MaxAttempts int NOT NULL COMMENT '最大尝试次数',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_identity_account_challenge PRIMARY KEY (ChallengeId),
    CONSTRAINT CK_fn_identity_account_challenge_CredentialHash CHECK (CHAR_LENGTH(CredentialHash) = 64),
    CONSTRAINT CK_fn_identity_account_challenge_MaxAttempts CHECK (MaxAttempts BETWEEN 1 AND 20),
    KEY IX_fn_identity_account_challenge_Purpose_NormalizedEmai_e46531a5 (Purpose, NormalizedEmail, CreatedAtUtc DESC, ChallengeId)
) COMMENT='身份认证账号操作挑战表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
