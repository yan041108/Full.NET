-- 222：注册策略三态模式与注册邀请表。

SET @registration_mode_exists := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_registration_policy'
      AND COLUMN_NAME = 'RegistrationMode'
);
SET @ddl := IF(
    @registration_mode_exists = 0,
    'ALTER TABLE fn_identity_registration_policy ADD COLUMN RegistrationMode tinyint NOT NULL DEFAULT 1 COMMENT ''注册模式：0=禁用，1=仅邀请，2=开放'' AFTER IsPublicRegistrationEnabled',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE fn_identity_registration_policy
SET RegistrationMode = CASE WHEN IsPublicRegistrationEnabled = 1 THEN 2 ELSE 1 END
WHERE RegistrationMode = 1
  AND IsPublicRegistrationEnabled = 1;

CREATE TABLE IF NOT EXISTS fn_identity_registration_invitation (
    InvitationId BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '目标租户标识',
    NormalizedEmail varchar(320) NOT NULL COMMENT '规范化邮箱',
    CredentialHash char(64) NOT NULL COMMENT '邀请凭据摘要',
    RegistrationWayId BINARY(16) NOT NULL COMMENT '注册方式标识',
    Status tinyint NOT NULL DEFAULT 0 COMMENT '邀请状态',
    BoundUserId BINARY(16) NULL COMMENT '已绑定账号标识',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期时间(UTC)',
    ConsumedAtUtc datetime(6) NULL COMMENT '凭据消费时间(UTC)',
    RevokedAtUtc datetime(6) NULL COMMENT '撤销时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_registration_invitation PRIMARY KEY (InvitationId),
    CONSTRAINT CK_fn_identity_registration_invitation_CredentialHash CHECK (CHAR_LENGTH(CredentialHash) = 64),
    CONSTRAINT FK_fn_identity_registration_invitation_RegistrationWay
        FOREIGN KEY (RegistrationWayId) REFERENCES fn_identity_user_registration_way (Id),
    KEY IX_fn_identity_registration_invitation_TenantId_Normali_6d9152b8 (TenantId, NormalizedEmail, Status, CreatedAtUtc DESC, InvitationId)
) COMMENT='身份认证注册邀请表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
