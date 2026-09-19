-- 223: tenant lifecycle, membership and invitations.
SET @db := DATABASE();

SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'fn_tenancy_tenant' AND COLUMN_NAME = 'LifecycleStatus') = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN LifecycleStatus varchar(32) NOT NULL DEFAULT ''Active'' COMMENT ''生命周期状态''',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'fn_tenancy_tenant' AND COLUMN_NAME = 'OwnerUserId') = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN OwnerUserId BINARY(16) NULL COMMENT ''所有者用户标识''',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'fn_tenancy_tenant' AND COLUMN_NAME = 'ProvisioningStatus') = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN ProvisioningStatus varchar(32) NOT NULL DEFAULT ''Completed'' COMMENT ''开通状态''',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(
    (SELECT COUNT(*) FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'fn_tenancy_tenant' AND COLUMN_NAME = 'ProvisioningStep') = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN ProvisioningStep varchar(64) NULL COMMENT ''开通步骤''',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

CREATE TABLE IF NOT EXISTS fn_identity_tenant_member (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    MemberRole varchar(32) NOT NULL COMMENT '成员角色',
    Status varchar(32) NOT NULL COMMENT '成员状态',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_tenant_member PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_tenant_member_TenantUser (TenantId, UserId),
    KEY IX_fn_identity_tenant_member_TenantId (TenantId)
) COMMENT='身份租户成员表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_tenant_invitation (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识',
    TargetEmail varchar(320) NOT NULL COMMENT '目标邮箱',
    TargetUserId BINARY(16) NULL COMMENT '目标用户标识',
    InvitedByUserId BINARY(16) NOT NULL COMMENT '邀请人用户标识',
    MemberRole varchar(32) NOT NULL COMMENT '成员角色',
    TokenHash varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '邀请凭据摘要',
    Status varchar(32) NOT NULL COMMENT '邀请状态',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_tenant_invitation PRIMARY KEY (Id),
    KEY IX_fn_identity_tenant_invitation_TenantEmail (TenantId, TargetEmail),
    UNIQUE KEY UX_fn_identity_tenant_invitation_TokenHash (TokenHash)
) COMMENT='身份租户邀请表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
