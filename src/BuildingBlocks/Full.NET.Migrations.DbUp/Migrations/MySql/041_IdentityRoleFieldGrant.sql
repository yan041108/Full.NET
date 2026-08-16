-- 041：角色字段授权只保存稳定语义键，作用域和租户边界始终从角色表验证。
CREATE TABLE IF NOT EXISTS fn_identity_role_field_grant (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    RoleId BINARY(16) NOT NULL COMMENT '角色标识',
    ResourceKey varchar(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '资源键',
    FieldKey varchar(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '字段键',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CONSTRAINT PK_fn_identity_role_field_grant PRIMARY KEY (Id),
    CONSTRAINT FK_fn_identity_role_field_grant_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role(Id),
    CONSTRAINT CK_fn_identity_role_field_grant_ResourceKey
        CHECK (CHAR_LENGTH(ResourceKey) BETWEEN 3 AND 160),
    CONSTRAINT CK_fn_identity_role_field_grant_FieldKey
        CHECK (CHAR_LENGTH(FieldKey) BETWEEN 1 AND 160),
    KEY IX_fn_identity_role_field_grant_RoleId (RoleId),
    UNIQUE KEY UX_fn_identity_role_field_grant_RoleResourceField
        (RoleId, ResourceKey, FieldKey)
) COMMENT='身份认证角色字段授权表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_identity_role_field_grant_migrate;
DELIMITER $$
CREATE PROCEDURE fn_identity_role_field_grant_migrate()
BEGIN
    -- 外键支撑索引必须独立存在，否则 MySQL 会拒绝恢复复合唯一索引。
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_role_field_grant'
          AND INDEX_NAME = 'IX_fn_identity_role_field_grant_RoleId'
    ) THEN
        ALTER TABLE fn_identity_role_field_grant
            ADD INDEX IX_fn_identity_role_field_grant_RoleId (RoleId);
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_role_field_grant'
          AND INDEX_NAME = 'UX_fn_identity_role_field_grant_RoleResourceField'
    )
    AND
    (
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_identity_role_field_grant'
              AND INDEX_NAME = 'UX_fn_identity_role_field_grant_RoleResourceField'
        ) <> 3
        OR
        (
        SELECT COUNT(*)
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_role_field_grant'
          AND INDEX_NAME = 'UX_fn_identity_role_field_grant_RoleResourceField'
          AND NON_UNIQUE = 0
          AND SUB_PART IS NULL
          AND
          (
              (SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'RoleId')
              OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'ResourceKey')
              OR (SEQ_IN_INDEX = 3 AND COLUMN_NAME = 'FieldKey')
          )
        ) <> 3
    ) THEN
        ALTER TABLE fn_identity_role_field_grant
            DROP INDEX UX_fn_identity_role_field_grant_RoleResourceField;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_role_field_grant'
          AND INDEX_NAME = 'UX_fn_identity_role_field_grant_RoleResourceField'
    ) THEN
        ALTER TABLE fn_identity_role_field_grant
            ADD UNIQUE INDEX UX_fn_identity_role_field_grant_RoleResourceField
                (RoleId, ResourceKey, FieldKey);
    END IF;
END$$
DELIMITER ;

CALL fn_identity_role_field_grant_migrate();
DROP PROCEDURE fn_identity_role_field_grant_migrate;
