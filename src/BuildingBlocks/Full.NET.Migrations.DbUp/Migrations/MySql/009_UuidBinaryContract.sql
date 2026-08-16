-- 009 在维护窗口内执行不可逆的 UUID Contract；开始后回退必须恢复数据库备份。
-- MySQL 最终状态只允许 RFC 9562 网络字节序 BINARY(16)，禁止 time-swap 与关闭外键检查。
CREATE TEMPORARY TABLE IF NOT EXISTS fn_uuid_contract_column
(
    Ordinal int NOT NULL,
    TableName varchar(64) NOT NULL,
    ColumnName varchar(64) NOT NULL,
    IsNullable boolean NOT NULL,
    ReferencedTableName varchar(64) NULL,
    ReferencedColumnName varchar(64) NULL,
    PRIMARY KEY (Ordinal),
    UNIQUE KEY UX_fn_uuid_contract_column_Table_Column (TableName, ColumnName)
);

DELETE FROM fn_uuid_contract_column;
INSERT INTO fn_uuid_contract_column
    (Ordinal, TableName, ColumnName, IsNullable, ReferencedTableName, ReferencedColumnName)
VALUES
    (1, 'fn_tenant_tenant', 'Id', false, NULL, NULL),
    (2, 'fn_outbox_message', 'Id', false, NULL, NULL),
    (3, 'fn_outbox_message', 'TenantId', true, 'fn_tenant_tenant', 'Id'),
    (4, 'fn_outbox_message', 'LockId', true, NULL, NULL),
    (5, 'fn_identity_user', 'Id', false, NULL, NULL),
    (6, 'fn_identity_user', 'TenantId', true, 'fn_tenant_tenant', 'Id'),
    (7, 'fn_identity_refresh_session', 'Id', false, NULL, NULL),
    (8, 'fn_identity_refresh_session', 'UserId', false, 'fn_identity_user', 'Id'),
    (9, 'fn_identity_refresh_session', 'FamilyId', false, NULL, NULL),
    (10, 'fn_identity_refresh_session', 'ReplacedById', true, 'fn_identity_refresh_session', 'Id'),
    (11, 'fn_identity_refresh_session', 'ActiveTenantId', true, 'fn_tenant_tenant', 'Id'),
    (12, 'fn_identity_auth_audit', 'Id', false, NULL, NULL),
    (13, 'fn_identity_auth_audit', 'UserId', true, 'fn_identity_user', 'Id'),
    (14, 'fn_identity_auth_audit', 'SessionId', true, 'fn_identity_refresh_session', 'Id'),
    (15, 'fn_identity_auth_audit', 'ContextTenantId', true, 'fn_tenant_tenant', 'Id'),
    (16, 'fn_identity_auth_audit', 'ActorUserId', true, 'fn_identity_user', 'Id'),
    (17, 'fn_identity_role', 'Id', false, NULL, NULL),
    (18, 'fn_identity_role', 'TenantId', true, 'fn_tenant_tenant', 'Id'),
    (19, 'fn_identity_user_role', 'UserId', false, 'fn_identity_user', 'Id'),
    (20, 'fn_identity_user_role', 'RoleId', false, 'fn_identity_role', 'Id'),
    (21, 'fn_identity_role_permission', 'RoleId', false, 'fn_identity_role', 'Id'),
    (22, 'fn_seed_run', 'Id', false, NULL, NULL),
    (23, 'fn_seed_run_item', 'RunId', false, 'fn_seed_run', 'Id');

-- 状态行只在完整预检后创建；Contracting 允许未记账的隐式提交 DDL 安全收敛。
DROP PROCEDURE IF EXISTS fn_uuid_binary_contract_gate;
DELIMITER $$
CREATE PROCEDURE fn_uuid_binary_contract_gate()
BEGIN
    DECLARE done boolean DEFAULT false;
    DECLARE current_table varchar(64);
    DECLARE current_column varchar(64);
    DECLARE current_nullable boolean;
    DECLARE referenced_table varchar(64);
    DECLARE referenced_column varchar(64);
    DECLARE shadow_column varchar(64);
    DECLARE state_mode varchar(16) DEFAULT NULL;
    DECLARE state_approval_id varchar(64) DEFAULT NULL;
    DECLARE diagnostic_message varchar(128);
    DECLARE column_cursor CURSOR FOR
        SELECT TableName, ColumnName, IsNullable, ReferencedTableName, ReferencedColumnName
        FROM fn_uuid_contract_column
        ORDER BY Ordinal;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = true;

    IF '$UuidContractMaintenanceMode$' <> '1' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID contract gate missing: maintenance mode';
    END IF;
    IF '$UuidContractBackupVerified$' <> '1' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID contract gate missing: verified backup';
    END IF;
    IF '$UuidContractLegacyWritersStopped$' <> '1' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID contract gate missing: legacy writers stopped';
    END IF;
    IF '$UuidContractDestructiveDdlApprovalId$' = '' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID contract gate missing: destructive DDL approval';
    END IF;
    IF NOT EXISTS(
        SELECT 1 FROM schemaversions
        WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql') THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID contract prerequisite missing: 008 expand journal';
    END IF;

    IF EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_uuid_contract_state') THEN
        IF EXISTS(SELECT 1 FROM fn_uuid_contract_state WHERE Id = 1) THEN
            SELECT SchemaMode, DestructiveDdlApprovalId
            INTO state_mode, state_approval_id
            FROM fn_uuid_contract_state
            WHERE Id = 1;
        END IF;
    END IF;

    IF state_mode IS NOT NULL THEN
        IF state_mode NOT IN ('Contracting', 'Binary16') THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID contract state schema mismatch';
        END IF;
        IF state_approval_id <> '$UuidContractDestructiveDdlApprovalId$' THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID contract approval mismatch';
        END IF;
    ELSE
        SET done = false;
        IF (SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TRIGGERS
            WHERE TRIGGER_SCHEMA = DATABASE()
              AND TRIGGER_NAME IN
              (
                  'TR_fn_tenant_tenant_UuidBinary_BI', 'TR_fn_tenant_tenant_UuidBinary_BU',
                  'TR_fn_outbox_message_UuidBinary_BI', 'TR_fn_outbox_message_UuidBinary_BU',
                  'TR_fn_identity_user_UuidBinary_BI', 'TR_fn_identity_user_UuidBinary_BU',
                  'TR_fn_identity_refresh_session_UuidBinary_BI', 'TR_fn_identity_refresh_session_UuidBinary_BU',
                  'TR_fn_identity_auth_audit_UuidBinary_BI', 'TR_fn_identity_auth_audit_UuidBinary_BU',
                  'TR_fn_identity_role_UuidBinary_BI', 'TR_fn_identity_role_UuidBinary_BU',
                  'TR_fn_identity_user_role_UuidBinary_BI', 'TR_fn_identity_user_role_UuidBinary_BU',
                  'TR_fn_identity_role_permission_UuidBinary_BI', 'TR_fn_identity_role_permission_UuidBinary_BU',
                  'TR_fn_seed_run_UuidBinary_BI', 'TR_fn_seed_run_UuidBinary_BU',
                  'TR_fn_seed_run_item_UuidBinary_BI', 'TR_fn_seed_run_item_UuidBinary_BU'
              )) <> 20 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID contract prerequisite missing: expand triggers';
        END IF;

        OPEN column_cursor;
        preflight_loop: LOOP
            FETCH column_cursor
            INTO current_table, current_column, current_nullable, referenced_table, referenced_column;
            IF done THEN
                LEAVE preflight_loop;
            END IF;
            SET shadow_column = CONCAT(current_column, 'Binary');

            IF NOT EXISTS(
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                  AND COLUMN_NAME = current_column AND DATA_TYPE = 'char'
                  AND CHARACTER_MAXIMUM_LENGTH = 36) THEN
                SET diagnostic_message = CONCAT(
                    'UUID canonical schema mismatch: ', current_table, '.', current_column);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            END IF;
            IF NOT EXISTS(
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                  AND COLUMN_NAME = shadow_column AND DATA_TYPE = 'binary'
                  AND CHARACTER_MAXIMUM_LENGTH = 16) THEN
                SET diagnostic_message = CONCAT(
                    'UUID shadow schema mismatch: ', current_table, '.', shadow_column);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            END IF;

            SET @fn_uuid_count = 0;
            SET @fn_uuid_sql = CONCAT(
                'SELECT COUNT(*) INTO @fn_uuid_count FROM `', current_table,
                '` WHERE (`', current_column, '` IS NULL) <> (`', shadow_column,
                '` IS NULL) OR (`', current_column, '` IS NOT NULL AND `', shadow_column,
                '` <> UUID_TO_BIN(`', current_column, '`, 0))');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
            IF @fn_uuid_count > 0 THEN
                SET diagnostic_message = CONCAT(
                    'UUID contract data mismatch: ', current_table, '.', current_column,
                    ' count=', @fn_uuid_count);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            END IF;

            IF referenced_table IS NOT NULL THEN
                SET @fn_uuid_count = 0;
                SET @fn_uuid_sql = CONCAT(
                    'SELECT COUNT(*) INTO @fn_uuid_count FROM `', current_table, '` child ',
                    'LEFT JOIN `', referenced_table, '` parent ON parent.`', referenced_column,
                    'Binary` = child.`', shadow_column, '` WHERE child.`', shadow_column,
                    '` IS NOT NULL AND parent.`', referenced_column, 'Binary` IS NULL');
                PREPARE fn_uuid_statement FROM @fn_uuid_sql;
                EXECUTE fn_uuid_statement;
                DEALLOCATE PREPARE fn_uuid_statement;
                IF @fn_uuid_count > 0 THEN
                    SET diagnostic_message = CONCAT(
                        'UUID contract reference mismatch: ', current_table, '.', current_column,
                        ' count=', @fn_uuid_count);
                    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
                END IF;
            END IF;
        END LOOP;
        CLOSE column_cursor;
    END IF;
END$$
DELIMITER ;

CALL fn_uuid_binary_contract_gate();
DROP PROCEDURE fn_uuid_binary_contract_gate;

CREATE TABLE IF NOT EXISTS fn_uuid_contract_state (
    Id tinyint NOT NULL COMMENT '逻辑主键',
    SchemaMode varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'Schema 模式',
    DestructiveDdlApprovalId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '破坏性 DDL 审批标识',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_uuid_contract_state PRIMARY KEY (Id),
    CONSTRAINT CK_fn_uuid_contract_state_Id CHECK (Id = 1),
    CONSTRAINT CK_fn_uuid_contract_state_SchemaMode CHECK (SchemaMode IN ('Contracting', 'Binary16'))
) COMMENT='UUID 二进制契约迁移状态' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- 未记账失败可能只留下状态表；重跑必须补齐状态约束后才能持久化 Contracting。
DROP PROCEDURE IF EXISTS fn_uuid_contract_state_converge;
DELIMITER $$
CREATE PROCEDURE fn_uuid_contract_state_converge()
BEGIN
    IF NOT EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_uuid_contract_state'
          AND CONSTRAINT_NAME = 'CK_fn_uuid_contract_state_Id'
          AND CONSTRAINT_TYPE = 'CHECK') THEN
        ALTER TABLE fn_uuid_contract_state
            ADD CONSTRAINT CK_fn_uuid_contract_state_Id CHECK (Id = 1);
    END IF;
    IF NOT EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_uuid_contract_state'
          AND CONSTRAINT_NAME = 'CK_fn_uuid_contract_state_SchemaMode'
          AND CONSTRAINT_TYPE = 'CHECK') THEN
        ALTER TABLE fn_uuid_contract_state
            ADD CONSTRAINT CK_fn_uuid_contract_state_SchemaMode
            CHECK (SchemaMode IN ('Contracting', 'Binary16'));
    END IF;
END$$
DELIMITER ;

CALL fn_uuid_contract_state_converge();
DROP PROCEDURE fn_uuid_contract_state_converge;

INSERT INTO fn_uuid_contract_state
    (Id, SchemaMode, DestructiveDdlApprovalId, UpdatedAtUtc)
VALUES
    (1, 'Contracting', '$UuidContractDestructiveDdlApprovalId$', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    SchemaMode = 'Contracting',
    UpdatedAtUtc = VALUES(UpdatedAtUtc);

-- 一旦持久化 Contracting，触发器和约束的每个隐式提交步骤都必须允许未记账重跑。
DROP TRIGGER IF EXISTS `TR_fn_tenant_tenant_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_tenant_tenant_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_outbox_message_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_outbox_message_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_identity_user_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_identity_user_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_identity_refresh_session_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_identity_refresh_session_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_identity_auth_audit_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_identity_auth_audit_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_identity_role_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_identity_role_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_identity_user_role_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_identity_user_role_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_identity_role_permission_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_identity_role_permission_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_seed_run_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_seed_run_UuidBinary_BU`;
DROP TRIGGER IF EXISTS `TR_fn_seed_run_item_UuidBinary_BI`;
DROP TRIGGER IF EXISTS `TR_fn_seed_run_item_UuidBinary_BU`;

DROP PROCEDURE IF EXISTS fn_uuid_binary_contract;
DELIMITER $$
CREATE PROCEDURE fn_uuid_binary_contract()
BEGIN
    DECLARE done boolean DEFAULT false;
    DECLARE current_table varchar(64);
    DECLARE current_column varchar(64);
    DECLARE current_nullable boolean;
    DECLARE referenced_table varchar(64);
    DECLARE referenced_column varchar(64);
    DECLARE legacy_column varchar(64);
    DECLARE shadow_column varchar(64);
    DECLARE diagnostic_message varchar(128);
    DECLARE column_cursor CURSOR FOR
        SELECT TableName, ColumnName, IsNullable, ReferencedTableName, ReferencedColumnName
        FROM fn_uuid_contract_column
        ORDER BY Ordinal;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = true;

    -- 先按依赖图移除引用约束；禁止通过 FOREIGN_KEY_CHECKS 绕过关系核对。
    IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
              WHERE CONSTRAINT_SCHEMA = DATABASE()
                AND CONSTRAINT_NAME = 'FK_fn_identity_refresh_session_User') THEN
        ALTER TABLE fn_identity_refresh_session
            DROP FOREIGN KEY FK_fn_identity_refresh_session_User;
    END IF;
    IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
              WHERE CONSTRAINT_SCHEMA = DATABASE()
                AND CONSTRAINT_NAME = 'FK_fn_identity_auth_audit_User') THEN
        ALTER TABLE fn_identity_auth_audit
            DROP FOREIGN KEY FK_fn_identity_auth_audit_User;
    END IF;
    IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
              WHERE CONSTRAINT_SCHEMA = DATABASE()
                AND CONSTRAINT_NAME = 'FK_fn_identity_user_role_User') THEN
        ALTER TABLE fn_identity_user_role DROP FOREIGN KEY FK_fn_identity_user_role_User;
    END IF;
    IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
              WHERE CONSTRAINT_SCHEMA = DATABASE()
                AND CONSTRAINT_NAME = 'FK_fn_identity_user_role_Role') THEN
        ALTER TABLE fn_identity_user_role DROP FOREIGN KEY FK_fn_identity_user_role_Role;
    END IF;
    IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
              WHERE CONSTRAINT_SCHEMA = DATABASE()
                AND CONSTRAINT_NAME = 'FK_fn_identity_role_permission_Role') THEN
        ALTER TABLE fn_identity_role_permission
            DROP FOREIGN KEY FK_fn_identity_role_permission_Role;
    END IF;
    IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
              WHERE CONSTRAINT_SCHEMA = DATABASE()
                AND CONSTRAINT_NAME = 'FK_fn_seed_run_item_Run') THEN
        ALTER TABLE fn_seed_run_item DROP FOREIGN KEY FK_fn_seed_run_item_Run;
    END IF;
    IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
              WHERE CONSTRAINT_SCHEMA = DATABASE()
                AND TABLE_NAME = 'fn_identity_role'
                AND CONSTRAINT_NAME = 'CK_fn_identity_role_SuperAdministratorScope') THEN
        ALTER TABLE fn_identity_role
            DROP CHECK CK_fn_identity_role_SuperAdministratorScope;
    END IF;

    -- 主键与 UUID 查询索引必须脱离 legacy 列后再按 canonical 列显式重建。
    SET @fn_uuid_tables = 'fn_tenant_tenant,fn_outbox_message,fn_identity_user,fn_identity_refresh_session,fn_identity_auth_audit,fn_identity_role,fn_identity_user_role,fn_identity_role_permission,fn_seed_run,fn_seed_run_item';
    WHILE @fn_uuid_tables <> '' DO
        SET @fn_uuid_separator = LOCATE(',', @fn_uuid_tables);
        SET @fn_uuid_table = IF(@fn_uuid_separator = 0, @fn_uuid_tables,
            LEFT(@fn_uuid_tables, @fn_uuid_separator - 1));
        SET @fn_uuid_tables = IF(@fn_uuid_separator = 0, '',
            SUBSTRING(@fn_uuid_tables, @fn_uuid_separator + 1));
        IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = @fn_uuid_table
                    AND CONSTRAINT_TYPE = 'PRIMARY KEY') THEN
            SET @fn_uuid_sql = CONCAT('ALTER TABLE `', @fn_uuid_table, '` DROP PRIMARY KEY');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        END IF;
    END WHILE;

    SET @fn_uuid_indexes =
        'fn_identity_refresh_session:IX_fn_identity_refresh_session_Family,fn_identity_refresh_session:IX_fn_identity_refresh_session_User,fn_identity_auth_audit:IX_fn_identity_auth_audit_User,fn_identity_role:IX_fn_identity_role_Tenant,fn_identity_refresh_session:FK_fn_identity_refresh_session_User,fn_identity_auth_audit:FK_fn_identity_auth_audit_User,fn_identity_user_role:FK_fn_identity_user_role_User,fn_identity_user_role:FK_fn_identity_user_role_Role,fn_identity_role_permission:FK_fn_identity_role_permission_Role,fn_seed_run_item:FK_fn_seed_run_item_Run';
    WHILE @fn_uuid_indexes <> '' DO
        SET @fn_uuid_separator = LOCATE(',', @fn_uuid_indexes);
        SET @fn_uuid_pair = IF(@fn_uuid_separator = 0, @fn_uuid_indexes,
            LEFT(@fn_uuid_indexes, @fn_uuid_separator - 1));
        SET @fn_uuid_indexes = IF(@fn_uuid_separator = 0, '',
            SUBSTRING(@fn_uuid_indexes, @fn_uuid_separator + 1));
        SET @fn_uuid_colon = LOCATE(':', @fn_uuid_pair);
        SET @fn_uuid_table = LEFT(@fn_uuid_pair, @fn_uuid_colon - 1);
        SET @fn_uuid_index = SUBSTRING(@fn_uuid_pair, @fn_uuid_colon + 1);
        IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @fn_uuid_table
                    AND INDEX_NAME = @fn_uuid_index) THEN
            SET @fn_uuid_sql = CONCAT(
                'ALTER TABLE `', @fn_uuid_table, '` DROP INDEX `', @fn_uuid_index, '`');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        END IF;
    END WHILE;

    OPEN column_cursor;
    rename_loop: LOOP
        FETCH column_cursor
        INTO current_table, current_column, current_nullable, referenced_table, referenced_column;
        IF done THEN
            LEAVE rename_loop;
        END IF;
        SET legacy_column = CONCAT(current_column, 'Legacy');
        SET shadow_column = CONCAT(current_column, 'Binary');
        SET @fn_uuid_shadow_index = CONCAT('UX_', current_table, '_', shadow_column);

        IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                    AND INDEX_NAME = @fn_uuid_shadow_index) THEN
            SET @fn_uuid_sql = CONCAT('ALTER TABLE `', current_table, '` DROP INDEX `',
                @fn_uuid_shadow_index, '`');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        END IF;

        IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                    AND COLUMN_NAME = current_column AND DATA_TYPE = 'char'
                    AND CHARACTER_MAXIMUM_LENGTH = 36) THEN
            IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                        AND COLUMN_NAME = legacy_column) THEN
                SET diagnostic_message = CONCAT(
                    'UUID legacy schema mismatch: ', current_table, '.', legacy_column);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            END IF;
            SET @fn_uuid_sql = CONCAT('ALTER TABLE `', current_table, '` RENAME COLUMN `',
                current_column, '` TO `', legacy_column, '`');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        ELSEIF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                        AND COLUMN_NAME = current_column
                        AND NOT (DATA_TYPE = 'binary' AND CHARACTER_MAXIMUM_LENGTH = 16)) THEN
            SET diagnostic_message = CONCAT(
                'UUID canonical schema mismatch: ', current_table, '.', current_column);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;

        IF NOT EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                        AND COLUMN_NAME = current_column) THEN
            IF NOT EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                          WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                            AND COLUMN_NAME = shadow_column AND DATA_TYPE = 'binary'
                            AND CHARACTER_MAXIMUM_LENGTH = 16) THEN
                SET diagnostic_message = CONCAT(
                    'UUID shadow schema mismatch: ', current_table, '.', shadow_column);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            END IF;
            SET @fn_uuid_sql = CONCAT('ALTER TABLE `', current_table, '` RENAME COLUMN `',
                shadow_column, '` TO `', current_column, '`');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        ELSEIF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                        AND COLUMN_NAME = shadow_column) THEN
            SET diagnostic_message = CONCAT(
                'UUID canonical and shadow both exist: ', current_table, '.', current_column);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;

        IF NOT EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                        AND COLUMN_NAME = current_column AND DATA_TYPE = 'binary'
                        AND CHARACTER_MAXIMUM_LENGTH = 16) THEN
            SET diagnostic_message = CONCAT(
                'UUID canonical schema mismatch: ', current_table, '.', current_column);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;
        IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                    AND COLUMN_NAME = legacy_column
                    AND NOT (DATA_TYPE = 'char' AND CHARACTER_MAXIMUM_LENGTH = 36)) THEN
            SET diagnostic_message = CONCAT(
                'UUID legacy schema mismatch: ', current_table, '.', legacy_column);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;

        SET @fn_uuid_sql = CONCAT('ALTER TABLE `', current_table, '` MODIFY COLUMN `',
            current_column, '` BINARY(16) ', IF(current_nullable, 'NULL', 'NOT NULL'));
        PREPARE fn_uuid_statement FROM @fn_uuid_sql;
        EXECUTE fn_uuid_statement;
        DEALLOCATE PREPARE fn_uuid_statement;
    END LOOP;
    CLOSE column_cursor;

    -- 全部 canonical 列就位后再次核对引用，再恢复主键、外键与查询索引。
    SET done = false;
    OPEN column_cursor;
    reference_loop: LOOP
        FETCH column_cursor
        INTO current_table, current_column, current_nullable, referenced_table, referenced_column;
        IF done THEN
            LEAVE reference_loop;
        END IF;
        IF referenced_table IS NOT NULL THEN
            SET @fn_uuid_count = 0;
            SET @fn_uuid_sql = CONCAT(
                'SELECT COUNT(*) INTO @fn_uuid_count FROM `', current_table, '` child ',
                'LEFT JOIN `', referenced_table, '` parent ON parent.`', referenced_column,
                '` = child.`', current_column, '` WHERE child.`', current_column,
                '` IS NOT NULL AND parent.`', referenced_column, '` IS NULL');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
            IF @fn_uuid_count > 0 THEN
                SET diagnostic_message = CONCAT(
                    'UUID contract reference mismatch: ', current_table, '.', current_column,
                    ' count=', @fn_uuid_count);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            END IF;
        END IF;
    END LOOP;
    CLOSE column_cursor;

    ALTER TABLE fn_tenant_tenant
        ADD CONSTRAINT PK_fn_tenant_tenant PRIMARY KEY (Id);
    ALTER TABLE fn_outbox_message
        ADD CONSTRAINT PK_fn_outbox_message PRIMARY KEY (Id);
    ALTER TABLE fn_identity_user
        ADD CONSTRAINT PK_fn_identity_user PRIMARY KEY (Id);
    ALTER TABLE fn_identity_refresh_session
        ADD CONSTRAINT PK_fn_identity_refresh_session PRIMARY KEY (Id);
    ALTER TABLE fn_identity_auth_audit
        ADD CONSTRAINT PK_fn_identity_auth_audit PRIMARY KEY (Id);
    ALTER TABLE fn_identity_role
        ADD CONSTRAINT PK_fn_identity_role PRIMARY KEY (Id);
    ALTER TABLE fn_identity_user_role
        ADD CONSTRAINT PK_fn_identity_user_role PRIMARY KEY (UserId, RoleId);
    ALTER TABLE fn_identity_role_permission
        ADD CONSTRAINT PK_fn_identity_role_permission PRIMARY KEY (RoleId, PermissionCode);
    ALTER TABLE fn_seed_run
        ADD CONSTRAINT PK_fn_seed_run PRIMARY KEY (Id);
    ALTER TABLE fn_seed_run_item
        ADD CONSTRAINT PK_fn_seed_run_item PRIMARY KEY (RunId, Contributor);

    ALTER TABLE fn_identity_refresh_session
        ADD CONSTRAINT FK_fn_identity_refresh_session_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id);
    ALTER TABLE fn_identity_auth_audit
        ADD CONSTRAINT FK_fn_identity_auth_audit_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id);
    ALTER TABLE fn_identity_user_role
        ADD CONSTRAINT FK_fn_identity_user_role_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id),
        ADD CONSTRAINT FK_fn_identity_user_role_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role(Id);
    ALTER TABLE fn_identity_role_permission
        ADD CONSTRAINT FK_fn_identity_role_permission_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role(Id);
    ALTER TABLE fn_seed_run_item
        ADD CONSTRAINT FK_fn_seed_run_item_Run
        FOREIGN KEY (RunId) REFERENCES fn_seed_run(Id);
    ALTER TABLE fn_identity_role
        ADD CONSTRAINT CK_fn_identity_role_SuperAdministratorScope
        CHECK (IsSuperAdministrator = false
               OR (IsSystem = true AND TenantId IS NULL AND ScopeKey = 'host'));

    CREATE INDEX IX_fn_identity_refresh_session_Family
        ON fn_identity_refresh_session(FamilyId, RevokedAtUtc, ExpiresAtUtc);
    CREATE INDEX IX_fn_identity_refresh_session_User
        ON fn_identity_refresh_session(UserId, RevokedAtUtc, ExpiresAtUtc);
    CREATE INDEX IX_fn_identity_auth_audit_User
        ON fn_identity_auth_audit(UserId, OccurredAtUtc);
    CREATE INDEX IX_fn_identity_role_Tenant
        ON fn_identity_role(TenantId, IsActive);

    -- 只有全部 canonical 约束与查询索引已恢复，才允许删除最后的 legacy 数据。
    IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME IN
              ('fn_tenant_tenant', 'fn_outbox_message', 'fn_identity_user',
               'fn_identity_refresh_session', 'fn_identity_auth_audit', 'fn_identity_role',
               'fn_identity_user_role', 'fn_identity_role_permission',
               'fn_seed_run', 'fn_seed_run_item')
          AND CONSTRAINT_TYPE = 'PRIMARY KEY') <> 10 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'UUID contract constraint rebuild mismatch: primary keys';
    END IF;
    IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND CONSTRAINT_NAME IN
              ('FK_fn_identity_refresh_session_User', 'FK_fn_identity_auth_audit_User',
               'FK_fn_identity_user_role_User', 'FK_fn_identity_user_role_Role',
               'FK_fn_identity_role_permission_Role', 'FK_fn_seed_run_item_Run')) <> 6 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'UUID contract constraint rebuild mismatch: foreign keys';
    END IF;
    IF (SELECT COUNT(DISTINCT CONCAT(TABLE_NAME, ':', INDEX_NAME))
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND INDEX_NAME IN
              ('IX_fn_identity_refresh_session_Family',
               'IX_fn_identity_refresh_session_User',
               'IX_fn_identity_auth_audit_User', 'IX_fn_identity_role_Tenant')) <> 4 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'UUID contract index rebuild mismatch';
    END IF;
    IF NOT EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_role'
          AND CONSTRAINT_NAME = 'CK_fn_identity_role_SuperAdministratorScope'
          AND CONSTRAINT_TYPE = 'CHECK') THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'UUID contract constraint rebuild mismatch: role scope';
    END IF;

    SET done = false;
    OPEN column_cursor;
    cleanup_loop: LOOP
        FETCH column_cursor
        INTO current_table, current_column, current_nullable, referenced_table, referenced_column;
        IF done THEN
            LEAVE cleanup_loop;
        END IF;
        SET legacy_column = CONCAT(current_column, 'Legacy');
        IF EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
                    AND COLUMN_NAME = legacy_column) THEN
            SET @fn_uuid_sql = CONCAT('ALTER TABLE `', current_table, '` DROP COLUMN `',
                legacy_column, '`');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        END IF;
    END LOOP;
    CLOSE column_cursor;
END$$
DELIMITER ;

CALL fn_uuid_binary_contract();
DROP PROCEDURE fn_uuid_binary_contract;

UPDATE fn_uuid_contract_state
SET SchemaMode = 'Binary16',
    UpdatedAtUtc = UTC_TIMESTAMP(6)
WHERE Id = 1;

DROP TEMPORARY TABLE fn_uuid_contract_column;
