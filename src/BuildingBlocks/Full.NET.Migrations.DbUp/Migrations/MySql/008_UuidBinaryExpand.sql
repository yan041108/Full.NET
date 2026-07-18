-- 008 仅扩展 Binary16 影子列；旧 char(36) 列仍是 canonical，009 才切换契约。
CREATE TEMPORARY TABLE IF NOT EXISTS fn_uuid_expand_column
(
    Ordinal int NOT NULL,
    TableName varchar(64) NOT NULL,
    ColumnName varchar(64) NOT NULL,
    IsNullable boolean NOT NULL,
    IsUnique boolean NOT NULL,
    BackfillOrder varchar(160) NOT NULL,
    ReferencedTableName varchar(64) NULL,
    ReferencedColumnName varchar(64) NULL,
    PRIMARY KEY (Ordinal),
    UNIQUE KEY UX_fn_uuid_expand_column_Table_Column (TableName, ColumnName)
);

DELETE FROM fn_uuid_expand_column;
INSERT INTO fn_uuid_expand_column
    (Ordinal, TableName, ColumnName, IsNullable, IsUnique, BackfillOrder,
     ReferencedTableName, ReferencedColumnName)
VALUES
    (1, 'fn_tenant_tenant', 'Id', false, true, '`Id`', NULL, NULL),
    (2, 'fn_outbox_message', 'Id', false, true, '`Id`', NULL, NULL),
    (3, 'fn_outbox_message', 'TenantId', true, false, '`Id`', 'fn_tenant_tenant', 'Id'),
    (4, 'fn_outbox_message', 'LockId', true, false, '`Id`', NULL, NULL),
    (5, 'fn_identity_user', 'Id', false, true, '`Id`', NULL, NULL),
    (6, 'fn_identity_user', 'TenantId', true, false, '`Id`', 'fn_tenant_tenant', 'Id'),
    (7, 'fn_identity_refresh_session', 'Id', false, true, '`Id`', NULL, NULL),
    (8, 'fn_identity_refresh_session', 'UserId', false, false, '`Id`', 'fn_identity_user', 'Id'),
    (9, 'fn_identity_refresh_session', 'FamilyId', false, false, '`Id`', NULL, NULL),
    (10, 'fn_identity_refresh_session', 'ReplacedById', true, false, '`Id`', 'fn_identity_refresh_session', 'Id'),
    (11, 'fn_identity_refresh_session', 'ActiveTenantId', true, false, '`Id`', 'fn_tenant_tenant', 'Id'),
    (12, 'fn_identity_auth_audit', 'Id', false, true, '`Id`', NULL, NULL),
    (13, 'fn_identity_auth_audit', 'UserId', true, false, '`Id`', 'fn_identity_user', 'Id'),
    (14, 'fn_identity_auth_audit', 'SessionId', true, false, '`Id`', 'fn_identity_refresh_session', 'Id'),
    (15, 'fn_identity_auth_audit', 'ContextTenantId', true, false, '`Id`', 'fn_tenant_tenant', 'Id'),
    (16, 'fn_identity_auth_audit', 'ActorUserId', true, false, '`Id`', 'fn_identity_user', 'Id'),
    (17, 'fn_identity_role', 'Id', false, true, '`Id`', NULL, NULL),
    (18, 'fn_identity_role', 'TenantId', true, false, '`Id`', 'fn_tenant_tenant', 'Id'),
    (19, 'fn_identity_user_role', 'UserId', false, false, '`UserId`, `RoleId`', 'fn_identity_user', 'Id'),
    (20, 'fn_identity_user_role', 'RoleId', false, false, '`UserId`, `RoleId`', 'fn_identity_role', 'Id'),
    (21, 'fn_identity_role_permission', 'RoleId', false, false, '`RoleId`, `PermissionCode`', 'fn_identity_role', 'Id'),
    (22, 'fn_seed_run', 'Id', false, true, '`Id`', NULL, NULL),
    (23, 'fn_seed_run_item', 'RunId', false, false, '`RunId`, `Contributor`', 'fn_seed_run', 'Id');

-- CALL 失败会留下过程对象；先清理才能让 DbUp 未记账的失败重跑重新进入收敛逻辑。
DROP PROCEDURE IF EXISTS fn_uuid_binary_expand;
DELIMITER $$
CREATE PROCEDURE fn_uuid_binary_expand()
BEGIN
    DECLARE done boolean DEFAULT false;
    DECLARE current_table varchar(64);
    DECLARE current_column varchar(64);
    DECLARE current_nullable boolean;
    DECLARE current_unique boolean;
    DECLARE backfill_order varchar(160);
    DECLARE referenced_table varchar(64);
    DECLARE referenced_column varchar(64);
    DECLARE shadow_column varchar(64);
    DECLARE unique_index_name varchar(64);
    DECLARE diagnostic_message varchar(128);
    DECLARE affected_rows bigint DEFAULT 0;
    DECLARE column_cursor CURSOR FOR
        SELECT TableName, ColumnName, IsNullable, IsUnique, BackfillOrder,
               ReferencedTableName, ReferencedColumnName
        FROM fn_uuid_expand_column
        ORDER BY Ordinal;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = true;

    OPEN column_cursor;
    column_loop: LOOP
        FETCH column_cursor
        INTO current_table, current_column, current_nullable, current_unique, backfill_order,
             referenced_table, referenced_column;
        IF done THEN
            LEAVE column_loop;
        END IF;
        SET shadow_column = CONCAT(current_column, 'Binary');

        IF NOT EXISTS(
            SELECT 1
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = current_table
              AND COLUMN_NAME = current_column
              AND DATA_TYPE = 'char'
              AND CHARACTER_MAXIMUM_LENGTH = 36) THEN
            SET diagnostic_message = CONCAT('UUID source schema mismatch: ', current_table, '.', current_column);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;

        IF NOT EXISTS(
            SELECT 1
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = current_table
              AND COLUMN_NAME = shadow_column) THEN
            SET @fn_uuid_sql = CONCAT(
                'ALTER TABLE `', current_table, '` ADD COLUMN `', shadow_column, '` BINARY(16) NULL');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        ELSEIF NOT EXISTS(
            SELECT 1
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = current_table
              AND COLUMN_NAME = shadow_column
              AND DATA_TYPE = 'binary'
              AND CHARACTER_MAXIMUM_LENGTH = 16) THEN
            SET diagnostic_message = CONCAT('UUID shadow schema mismatch: ', current_table, '.', shadow_column);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;
    END LOOP;
    CLOSE column_cursor;

    SET done = false;
    OPEN column_cursor;
    backfill_loop: LOOP
        FETCH column_cursor
        INTO current_table, current_column, current_nullable, current_unique, backfill_order,
             referenced_table, referenced_column;
        IF done THEN
            LEAVE backfill_loop;
        END IF;
        SET shadow_column = CONCAT(current_column, 'Binary');

        SET @fn_uuid_count = 0;
        SET @fn_uuid_sql = CONCAT(
            'SELECT COUNT(*) INTO @fn_uuid_count FROM `', current_table, '` WHERE `', current_column,
            '` IS NOT NULL AND (IS_UUID(`', current_column, '`) = 0 OR LOWER(`', current_column,
            '`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(`', current_column, '`, 0), 0)))');
        PREPARE fn_uuid_statement FROM @fn_uuid_sql;
        EXECUTE fn_uuid_statement;
        DEALLOCATE PREPARE fn_uuid_statement;
        IF @fn_uuid_count > 0 THEN
            SET diagnostic_message = CONCAT(
                'Invalid UUID: ', current_table, '.', current_column, ' count=', @fn_uuid_count);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;

        SET @fn_uuid_count = 0;
        SET @fn_uuid_sql = CONCAT(
            'SELECT COUNT(*) INTO @fn_uuid_count FROM `', current_table, '` WHERE `', current_column,
            '` IS NOT NULL AND `', shadow_column, '` IS NOT NULL AND `', shadow_column,
            '` <> UUID_TO_BIN(`', current_column, '`, 0)');
        PREPARE fn_uuid_statement FROM @fn_uuid_sql;
        EXECUTE fn_uuid_statement;
        DEALLOCATE PREPARE fn_uuid_statement;
        IF @fn_uuid_count > 0 THEN
            SET diagnostic_message = CONCAT(
                'UUID shadow conflict: ', current_table, '.', current_column, ' count=', @fn_uuid_count);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;

        SET affected_rows = 1;
        WHILE affected_rows > 0 DO
            SET @fn_uuid_sql = CONCAT(
                'UPDATE `', current_table, '` SET `', shadow_column, '` = UUID_TO_BIN(`', current_column,
                '`, 0) WHERE `', current_column, '` IS NOT NULL AND `', shadow_column,
                '` IS NULL ORDER BY ', backfill_order, ' LIMIT 1000');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            SET affected_rows = ROW_COUNT();
            DEALLOCATE PREPARE fn_uuid_statement;
        END WHILE;

        SET @fn_uuid_count = 0;
        SET @fn_uuid_sql = CONCAT(
            'SELECT COUNT(*) INTO @fn_uuid_count FROM `', current_table, '` WHERE (`', current_column,
            '` IS NULL) <> (`', shadow_column, '` IS NULL) OR (`', current_column,
            '` IS NOT NULL AND `', shadow_column, '` <> UUID_TO_BIN(`', current_column, '`, 0))');
        PREPARE fn_uuid_statement FROM @fn_uuid_sql;
        EXECUTE fn_uuid_statement;
        DEALLOCATE PREPARE fn_uuid_statement;
        IF @fn_uuid_count > 0 THEN
            SET diagnostic_message = CONCAT(
                'UUID backfill incomplete: ', current_table, '.', current_column, ' count=', @fn_uuid_count);
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
        END IF;

        IF current_unique THEN
            SET @fn_uuid_count = 0;
            SET @fn_uuid_sql = CONCAT(
                'SELECT COUNT(*) - COUNT(DISTINCT `', shadow_column, '`) INTO @fn_uuid_count FROM `',
                current_table, '` WHERE `', current_column, '` IS NOT NULL');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
            IF @fn_uuid_count > 0 THEN
                SET diagnostic_message = CONCAT(
                    'Duplicate UUID binary: ', current_table, '.', current_column, ' count=', @fn_uuid_count);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            END IF;
        END IF;

        IF current_nullable AND EXISTS(
            SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
              AND COLUMN_NAME = shadow_column AND IS_NULLABLE = 'NO') THEN
            SET @fn_uuid_sql = CONCAT(
                'ALTER TABLE `', current_table, '` MODIFY COLUMN `', shadow_column, '` BINARY(16) NULL');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        ELSEIF NOT current_nullable AND EXISTS(
            SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = current_table
              AND COLUMN_NAME = shadow_column AND IS_NULLABLE = 'YES') THEN
            SET @fn_uuid_sql = CONCAT(
                'ALTER TABLE `', current_table, '` MODIFY COLUMN `', shadow_column, '` BINARY(16) NOT NULL');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
        END IF;

        IF current_unique THEN
            SET unique_index_name = CONCAT('UX_', current_table, '_', shadow_column);
            IF EXISTS(
                SELECT 1
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = current_table
                  AND INDEX_NAME = unique_index_name)
               AND (EXISTS(
                    SELECT 1
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = current_table
                      AND INDEX_NAME = unique_index_name
                      AND (NON_UNIQUE <> 0 OR COLUMN_NAME <> shadow_column OR SEQ_IN_INDEX <> 1
                           OR SUB_PART IS NOT NULL))
                    OR (SELECT COUNT(*)
                        FROM INFORMATION_SCHEMA.STATISTICS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = current_table
                          AND INDEX_NAME = unique_index_name) <> 1) THEN
                SET diagnostic_message = CONCAT('UUID unique index mismatch: ', unique_index_name);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            ELSEIF NOT EXISTS(
                SELECT 1
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = current_table
                  AND INDEX_NAME = unique_index_name
                  AND NON_UNIQUE = 0
                  AND COLUMN_NAME = shadow_column
                  AND SEQ_IN_INDEX = 1
                  AND SUB_PART IS NULL) THEN
                SET @fn_uuid_sql = CONCAT(
                    'ALTER TABLE `', current_table, '` ADD UNIQUE INDEX `', unique_index_name,
                    '` (`', shadow_column, '`)');
                PREPARE fn_uuid_statement FROM @fn_uuid_sql;
                EXECUTE fn_uuid_statement;
                DEALLOCATE PREPARE fn_uuid_statement;
            END IF;
        END IF;
    END LOOP;
    CLOSE column_cursor;

    SET done = false;
    OPEN column_cursor;
    reference_loop: LOOP
        FETCH column_cursor
        INTO current_table, current_column, current_nullable, current_unique, backfill_order,
             referenced_table, referenced_column;
        IF done THEN
            LEAVE reference_loop;
        END IF;
        IF referenced_table IS NOT NULL THEN
            SET @fn_uuid_count = 0;
            SET @fn_uuid_sql = CONCAT(
                'SELECT COUNT(*) INTO @fn_uuid_count FROM `', current_table, '` AS child ',
                'LEFT JOIN `', referenced_table, '` AS parent ON parent.`', referenced_column,
                'Binary` = child.`', current_column, 'Binary` WHERE child.`', current_column,
                'Binary` IS NOT NULL AND parent.`', referenced_column, 'Binary` IS NULL');
            PREPARE fn_uuid_statement FROM @fn_uuid_sql;
            EXECUTE fn_uuid_statement;
            DEALLOCATE PREPARE fn_uuid_statement;
            IF @fn_uuid_count > 0 THEN
                SET diagnostic_message = CONCAT(
                    'UUID reference missing: ', current_table, '.', current_column, ' count=', @fn_uuid_count);
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = diagnostic_message;
            END IF;
        END IF;
    END LOOP;
    CLOSE column_cursor;

END$$
DELIMITER ;

CALL fn_uuid_binary_expand();
DROP PROCEDURE fn_uuid_binary_expand;

-- 触发器使用固定名称，重跑时重建可恢复缺失对象，并保持旧应用写入与影子列同步。
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

DELIMITER $$
CREATE TRIGGER `TR_fn_tenant_tenant_UuidBinary_BI`
BEFORE INSERT ON `fn_tenant_tenant`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_tenant_tenant.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_tenant_tenant.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_tenant_tenant.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_tenant_tenant.Id';
    END IF;
END$$

CREATE TRIGGER `TR_fn_tenant_tenant_UuidBinary_BU`
BEFORE UPDATE ON `fn_tenant_tenant`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` <=> OLD.`IdBinary` THEN
            SET NEW.`IdBinary` = NULL;
        ELSEIF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_tenant_tenant.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_tenant_tenant.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_tenant_tenant.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        IF (NEW.`IdBinary` <=> OLD.`IdBinary`) AND NOT (NEW.`Id` <=> OLD.`Id`) THEN
            SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_tenant_tenant.Id';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_outbox_message_UuidBinary_BI`
BEFORE INSERT ON `fn_outbox_message`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.Id';
    END IF;
    IF NEW.`TenantId` IS NULL THEN
        IF NEW.`TenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.TenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`TenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.TenantId';
    ELSEIF LOWER(NEW.`TenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`TenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.TenantId';
    ELSEIF NEW.`TenantIdBinary` IS NULL THEN
        SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
    ELSEIF NEW.`TenantIdBinary` <> UUID_TO_BIN(NEW.`TenantId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.TenantId';
    END IF;
    IF NEW.`LockId` IS NULL THEN
        IF NEW.`LockIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.LockId';
        END IF;
    ELSEIF IS_UUID(NEW.`LockId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.LockId';
    ELSEIF LOWER(NEW.`LockId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`LockId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.LockId';
    ELSEIF NEW.`LockIdBinary` IS NULL THEN
        SET NEW.`LockIdBinary` = UUID_TO_BIN(NEW.`LockId`, 0);
    ELSEIF NEW.`LockIdBinary` <> UUID_TO_BIN(NEW.`LockId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.LockId';
    END IF;
END$$

CREATE TRIGGER `TR_fn_outbox_message_UuidBinary_BU`
BEFORE UPDATE ON `fn_outbox_message`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` <=> OLD.`IdBinary` THEN
            SET NEW.`IdBinary` = NULL;
        ELSEIF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        IF (NEW.`IdBinary` <=> OLD.`IdBinary`) AND NOT (NEW.`Id` <=> OLD.`Id`) THEN
            SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.Id';
        END IF;
    END IF;
    IF NEW.`TenantId` IS NULL THEN
        IF NEW.`TenantIdBinary` <=> OLD.`TenantIdBinary` THEN
            SET NEW.`TenantIdBinary` = NULL;
        ELSEIF NEW.`TenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.TenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`TenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.TenantId';
    ELSEIF LOWER(NEW.`TenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`TenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.TenantId';
    ELSEIF NEW.`TenantIdBinary` IS NULL THEN
        SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
    ELSEIF NEW.`TenantIdBinary` <> UUID_TO_BIN(NEW.`TenantId`, 0) THEN
        IF (NEW.`TenantIdBinary` <=> OLD.`TenantIdBinary`) AND NOT (NEW.`TenantId` <=> OLD.`TenantId`) THEN
            SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.TenantId';
        END IF;
    END IF;
    IF NEW.`LockId` IS NULL THEN
        IF NEW.`LockIdBinary` <=> OLD.`LockIdBinary` THEN
            SET NEW.`LockIdBinary` = NULL;
        ELSEIF NEW.`LockIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.LockId';
        END IF;
    ELSEIF IS_UUID(NEW.`LockId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.LockId';
    ELSEIF LOWER(NEW.`LockId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`LockId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_outbox_message.LockId';
    ELSEIF NEW.`LockIdBinary` IS NULL THEN
        SET NEW.`LockIdBinary` = UUID_TO_BIN(NEW.`LockId`, 0);
    ELSEIF NEW.`LockIdBinary` <> UUID_TO_BIN(NEW.`LockId`, 0) THEN
        IF (NEW.`LockIdBinary` <=> OLD.`LockIdBinary`) AND NOT (NEW.`LockId` <=> OLD.`LockId`) THEN
            SET NEW.`LockIdBinary` = UUID_TO_BIN(NEW.`LockId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_outbox_message.LockId';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_user_UuidBinary_BI`
BEFORE INSERT ON `fn_identity_user`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user.Id';
    END IF;
    IF NEW.`TenantId` IS NULL THEN
        IF NEW.`TenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user.TenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`TenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user.TenantId';
    ELSEIF LOWER(NEW.`TenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`TenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user.TenantId';
    ELSEIF NEW.`TenantIdBinary` IS NULL THEN
        SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
    ELSEIF NEW.`TenantIdBinary` <> UUID_TO_BIN(NEW.`TenantId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user.TenantId';
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_user_UuidBinary_BU`
BEFORE UPDATE ON `fn_identity_user`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` <=> OLD.`IdBinary` THEN
            SET NEW.`IdBinary` = NULL;
        ELSEIF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        IF (NEW.`IdBinary` <=> OLD.`IdBinary`) AND NOT (NEW.`Id` <=> OLD.`Id`) THEN
            SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user.Id';
        END IF;
    END IF;
    IF NEW.`TenantId` IS NULL THEN
        IF NEW.`TenantIdBinary` <=> OLD.`TenantIdBinary` THEN
            SET NEW.`TenantIdBinary` = NULL;
        ELSEIF NEW.`TenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user.TenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`TenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user.TenantId';
    ELSEIF LOWER(NEW.`TenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`TenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user.TenantId';
    ELSEIF NEW.`TenantIdBinary` IS NULL THEN
        SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
    ELSEIF NEW.`TenantIdBinary` <> UUID_TO_BIN(NEW.`TenantId`, 0) THEN
        IF (NEW.`TenantIdBinary` <=> OLD.`TenantIdBinary`) AND NOT (NEW.`TenantId` <=> OLD.`TenantId`) THEN
            SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user.TenantId';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_refresh_session_UuidBinary_BI`
BEFORE INSERT ON `fn_identity_refresh_session`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.Id';
    END IF;
    IF NEW.`UserId` IS NULL THEN
        IF NEW.`UserIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.UserId';
        END IF;
    ELSEIF IS_UUID(NEW.`UserId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.UserId';
    ELSEIF LOWER(NEW.`UserId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`UserId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.UserId';
    ELSEIF NEW.`UserIdBinary` IS NULL THEN
        SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
    ELSEIF NEW.`UserIdBinary` <> UUID_TO_BIN(NEW.`UserId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.UserId';
    END IF;
    IF NEW.`FamilyId` IS NULL THEN
        IF NEW.`FamilyIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.FamilyId';
        END IF;
    ELSEIF IS_UUID(NEW.`FamilyId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.FamilyId';
    ELSEIF LOWER(NEW.`FamilyId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`FamilyId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.FamilyId';
    ELSEIF NEW.`FamilyIdBinary` IS NULL THEN
        SET NEW.`FamilyIdBinary` = UUID_TO_BIN(NEW.`FamilyId`, 0);
    ELSEIF NEW.`FamilyIdBinary` <> UUID_TO_BIN(NEW.`FamilyId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.FamilyId';
    END IF;
    IF NEW.`ReplacedById` IS NULL THEN
        IF NEW.`ReplacedByIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.ReplacedById';
        END IF;
    ELSEIF IS_UUID(NEW.`ReplacedById`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.ReplacedById';
    ELSEIF LOWER(NEW.`ReplacedById`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`ReplacedById`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.ReplacedById';
    ELSEIF NEW.`ReplacedByIdBinary` IS NULL THEN
        SET NEW.`ReplacedByIdBinary` = UUID_TO_BIN(NEW.`ReplacedById`, 0);
    ELSEIF NEW.`ReplacedByIdBinary` <> UUID_TO_BIN(NEW.`ReplacedById`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.ReplacedById';
    END IF;
    IF NEW.`ActiveTenantId` IS NULL THEN
        IF NEW.`ActiveTenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.ActiveTenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`ActiveTenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.ActiveTenantId';
    ELSEIF LOWER(NEW.`ActiveTenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`ActiveTenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.ActiveTenantId';
    ELSEIF NEW.`ActiveTenantIdBinary` IS NULL THEN
        SET NEW.`ActiveTenantIdBinary` = UUID_TO_BIN(NEW.`ActiveTenantId`, 0);
    ELSEIF NEW.`ActiveTenantIdBinary` <> UUID_TO_BIN(NEW.`ActiveTenantId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.ActiveTenantId';
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_refresh_session_UuidBinary_BU`
BEFORE UPDATE ON `fn_identity_refresh_session`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` <=> OLD.`IdBinary` THEN
            SET NEW.`IdBinary` = NULL;
        ELSEIF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        IF (NEW.`IdBinary` <=> OLD.`IdBinary`) AND NOT (NEW.`Id` <=> OLD.`Id`) THEN
            SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.Id';
        END IF;
    END IF;
    IF NEW.`UserId` IS NULL THEN
        IF NEW.`UserIdBinary` <=> OLD.`UserIdBinary` THEN
            SET NEW.`UserIdBinary` = NULL;
        ELSEIF NEW.`UserIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.UserId';
        END IF;
    ELSEIF IS_UUID(NEW.`UserId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.UserId';
    ELSEIF LOWER(NEW.`UserId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`UserId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.UserId';
    ELSEIF NEW.`UserIdBinary` IS NULL THEN
        SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
    ELSEIF NEW.`UserIdBinary` <> UUID_TO_BIN(NEW.`UserId`, 0) THEN
        IF (NEW.`UserIdBinary` <=> OLD.`UserIdBinary`) AND NOT (NEW.`UserId` <=> OLD.`UserId`) THEN
            SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.UserId';
        END IF;
    END IF;
    IF NEW.`FamilyId` IS NULL THEN
        IF NEW.`FamilyIdBinary` <=> OLD.`FamilyIdBinary` THEN
            SET NEW.`FamilyIdBinary` = NULL;
        ELSEIF NEW.`FamilyIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.FamilyId';
        END IF;
    ELSEIF IS_UUID(NEW.`FamilyId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.FamilyId';
    ELSEIF LOWER(NEW.`FamilyId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`FamilyId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.FamilyId';
    ELSEIF NEW.`FamilyIdBinary` IS NULL THEN
        SET NEW.`FamilyIdBinary` = UUID_TO_BIN(NEW.`FamilyId`, 0);
    ELSEIF NEW.`FamilyIdBinary` <> UUID_TO_BIN(NEW.`FamilyId`, 0) THEN
        IF (NEW.`FamilyIdBinary` <=> OLD.`FamilyIdBinary`) AND NOT (NEW.`FamilyId` <=> OLD.`FamilyId`) THEN
            SET NEW.`FamilyIdBinary` = UUID_TO_BIN(NEW.`FamilyId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.FamilyId';
        END IF;
    END IF;
    IF NEW.`ReplacedById` IS NULL THEN
        IF NEW.`ReplacedByIdBinary` <=> OLD.`ReplacedByIdBinary` THEN
            SET NEW.`ReplacedByIdBinary` = NULL;
        ELSEIF NEW.`ReplacedByIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.ReplacedById';
        END IF;
    ELSEIF IS_UUID(NEW.`ReplacedById`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.ReplacedById';
    ELSEIF LOWER(NEW.`ReplacedById`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`ReplacedById`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.ReplacedById';
    ELSEIF NEW.`ReplacedByIdBinary` IS NULL THEN
        SET NEW.`ReplacedByIdBinary` = UUID_TO_BIN(NEW.`ReplacedById`, 0);
    ELSEIF NEW.`ReplacedByIdBinary` <> UUID_TO_BIN(NEW.`ReplacedById`, 0) THEN
        IF (NEW.`ReplacedByIdBinary` <=> OLD.`ReplacedByIdBinary`) AND NOT (NEW.`ReplacedById` <=> OLD.`ReplacedById`) THEN
            SET NEW.`ReplacedByIdBinary` = UUID_TO_BIN(NEW.`ReplacedById`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.ReplacedById';
        END IF;
    END IF;
    IF NEW.`ActiveTenantId` IS NULL THEN
        IF NEW.`ActiveTenantIdBinary` <=> OLD.`ActiveTenantIdBinary` THEN
            SET NEW.`ActiveTenantIdBinary` = NULL;
        ELSEIF NEW.`ActiveTenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.ActiveTenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`ActiveTenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.ActiveTenantId';
    ELSEIF LOWER(NEW.`ActiveTenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`ActiveTenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_refresh_session.ActiveTenantId';
    ELSEIF NEW.`ActiveTenantIdBinary` IS NULL THEN
        SET NEW.`ActiveTenantIdBinary` = UUID_TO_BIN(NEW.`ActiveTenantId`, 0);
    ELSEIF NEW.`ActiveTenantIdBinary` <> UUID_TO_BIN(NEW.`ActiveTenantId`, 0) THEN
        IF (NEW.`ActiveTenantIdBinary` <=> OLD.`ActiveTenantIdBinary`) AND NOT (NEW.`ActiveTenantId` <=> OLD.`ActiveTenantId`) THEN
            SET NEW.`ActiveTenantIdBinary` = UUID_TO_BIN(NEW.`ActiveTenantId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_refresh_session.ActiveTenantId';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_auth_audit_UuidBinary_BI`
BEFORE INSERT ON `fn_identity_auth_audit`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.Id';
    END IF;
    IF NEW.`UserId` IS NULL THEN
        IF NEW.`UserIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.UserId';
        END IF;
    ELSEIF IS_UUID(NEW.`UserId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.UserId';
    ELSEIF LOWER(NEW.`UserId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`UserId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.UserId';
    ELSEIF NEW.`UserIdBinary` IS NULL THEN
        SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
    ELSEIF NEW.`UserIdBinary` <> UUID_TO_BIN(NEW.`UserId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.UserId';
    END IF;
    IF NEW.`SessionId` IS NULL THEN
        IF NEW.`SessionIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.SessionId';
        END IF;
    ELSEIF IS_UUID(NEW.`SessionId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.SessionId';
    ELSEIF LOWER(NEW.`SessionId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`SessionId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.SessionId';
    ELSEIF NEW.`SessionIdBinary` IS NULL THEN
        SET NEW.`SessionIdBinary` = UUID_TO_BIN(NEW.`SessionId`, 0);
    ELSEIF NEW.`SessionIdBinary` <> UUID_TO_BIN(NEW.`SessionId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.SessionId';
    END IF;
    IF NEW.`ContextTenantId` IS NULL THEN
        IF NEW.`ContextTenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.ContextTenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`ContextTenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.ContextTenantId';
    ELSEIF LOWER(NEW.`ContextTenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`ContextTenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.ContextTenantId';
    ELSEIF NEW.`ContextTenantIdBinary` IS NULL THEN
        SET NEW.`ContextTenantIdBinary` = UUID_TO_BIN(NEW.`ContextTenantId`, 0);
    ELSEIF NEW.`ContextTenantIdBinary` <> UUID_TO_BIN(NEW.`ContextTenantId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.ContextTenantId';
    END IF;
    IF NEW.`ActorUserId` IS NULL THEN
        IF NEW.`ActorUserIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.ActorUserId';
        END IF;
    ELSEIF IS_UUID(NEW.`ActorUserId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.ActorUserId';
    ELSEIF LOWER(NEW.`ActorUserId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`ActorUserId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.ActorUserId';
    ELSEIF NEW.`ActorUserIdBinary` IS NULL THEN
        SET NEW.`ActorUserIdBinary` = UUID_TO_BIN(NEW.`ActorUserId`, 0);
    ELSEIF NEW.`ActorUserIdBinary` <> UUID_TO_BIN(NEW.`ActorUserId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.ActorUserId';
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_auth_audit_UuidBinary_BU`
BEFORE UPDATE ON `fn_identity_auth_audit`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` <=> OLD.`IdBinary` THEN
            SET NEW.`IdBinary` = NULL;
        ELSEIF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        IF (NEW.`IdBinary` <=> OLD.`IdBinary`) AND NOT (NEW.`Id` <=> OLD.`Id`) THEN
            SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.Id';
        END IF;
    END IF;
    IF NEW.`UserId` IS NULL THEN
        IF NEW.`UserIdBinary` <=> OLD.`UserIdBinary` THEN
            SET NEW.`UserIdBinary` = NULL;
        ELSEIF NEW.`UserIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.UserId';
        END IF;
    ELSEIF IS_UUID(NEW.`UserId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.UserId';
    ELSEIF LOWER(NEW.`UserId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`UserId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.UserId';
    ELSEIF NEW.`UserIdBinary` IS NULL THEN
        SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
    ELSEIF NEW.`UserIdBinary` <> UUID_TO_BIN(NEW.`UserId`, 0) THEN
        IF (NEW.`UserIdBinary` <=> OLD.`UserIdBinary`) AND NOT (NEW.`UserId` <=> OLD.`UserId`) THEN
            SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.UserId';
        END IF;
    END IF;
    IF NEW.`SessionId` IS NULL THEN
        IF NEW.`SessionIdBinary` <=> OLD.`SessionIdBinary` THEN
            SET NEW.`SessionIdBinary` = NULL;
        ELSEIF NEW.`SessionIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.SessionId';
        END IF;
    ELSEIF IS_UUID(NEW.`SessionId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.SessionId';
    ELSEIF LOWER(NEW.`SessionId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`SessionId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.SessionId';
    ELSEIF NEW.`SessionIdBinary` IS NULL THEN
        SET NEW.`SessionIdBinary` = UUID_TO_BIN(NEW.`SessionId`, 0);
    ELSEIF NEW.`SessionIdBinary` <> UUID_TO_BIN(NEW.`SessionId`, 0) THEN
        IF (NEW.`SessionIdBinary` <=> OLD.`SessionIdBinary`) AND NOT (NEW.`SessionId` <=> OLD.`SessionId`) THEN
            SET NEW.`SessionIdBinary` = UUID_TO_BIN(NEW.`SessionId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.SessionId';
        END IF;
    END IF;
    IF NEW.`ContextTenantId` IS NULL THEN
        IF NEW.`ContextTenantIdBinary` <=> OLD.`ContextTenantIdBinary` THEN
            SET NEW.`ContextTenantIdBinary` = NULL;
        ELSEIF NEW.`ContextTenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.ContextTenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`ContextTenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.ContextTenantId';
    ELSEIF LOWER(NEW.`ContextTenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`ContextTenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.ContextTenantId';
    ELSEIF NEW.`ContextTenantIdBinary` IS NULL THEN
        SET NEW.`ContextTenantIdBinary` = UUID_TO_BIN(NEW.`ContextTenantId`, 0);
    ELSEIF NEW.`ContextTenantIdBinary` <> UUID_TO_BIN(NEW.`ContextTenantId`, 0) THEN
        IF (NEW.`ContextTenantIdBinary` <=> OLD.`ContextTenantIdBinary`) AND NOT (NEW.`ContextTenantId` <=> OLD.`ContextTenantId`) THEN
            SET NEW.`ContextTenantIdBinary` = UUID_TO_BIN(NEW.`ContextTenantId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.ContextTenantId';
        END IF;
    END IF;
    IF NEW.`ActorUserId` IS NULL THEN
        IF NEW.`ActorUserIdBinary` <=> OLD.`ActorUserIdBinary` THEN
            SET NEW.`ActorUserIdBinary` = NULL;
        ELSEIF NEW.`ActorUserIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.ActorUserId';
        END IF;
    ELSEIF IS_UUID(NEW.`ActorUserId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.ActorUserId';
    ELSEIF LOWER(NEW.`ActorUserId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`ActorUserId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_auth_audit.ActorUserId';
    ELSEIF NEW.`ActorUserIdBinary` IS NULL THEN
        SET NEW.`ActorUserIdBinary` = UUID_TO_BIN(NEW.`ActorUserId`, 0);
    ELSEIF NEW.`ActorUserIdBinary` <> UUID_TO_BIN(NEW.`ActorUserId`, 0) THEN
        IF (NEW.`ActorUserIdBinary` <=> OLD.`ActorUserIdBinary`) AND NOT (NEW.`ActorUserId` <=> OLD.`ActorUserId`) THEN
            SET NEW.`ActorUserIdBinary` = UUID_TO_BIN(NEW.`ActorUserId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_auth_audit.ActorUserId';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_role_UuidBinary_BI`
BEFORE INSERT ON `fn_identity_role`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role.Id';
    END IF;
    IF NEW.`TenantId` IS NULL THEN
        IF NEW.`TenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role.TenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`TenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role.TenantId';
    ELSEIF LOWER(NEW.`TenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`TenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role.TenantId';
    ELSEIF NEW.`TenantIdBinary` IS NULL THEN
        SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
    ELSEIF NEW.`TenantIdBinary` <> UUID_TO_BIN(NEW.`TenantId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role.TenantId';
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_role_UuidBinary_BU`
BEFORE UPDATE ON `fn_identity_role`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` <=> OLD.`IdBinary` THEN
            SET NEW.`IdBinary` = NULL;
        ELSEIF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        IF (NEW.`IdBinary` <=> OLD.`IdBinary`) AND NOT (NEW.`Id` <=> OLD.`Id`) THEN
            SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role.Id';
        END IF;
    END IF;
    IF NEW.`TenantId` IS NULL THEN
        IF NEW.`TenantIdBinary` <=> OLD.`TenantIdBinary` THEN
            SET NEW.`TenantIdBinary` = NULL;
        ELSEIF NEW.`TenantIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role.TenantId';
        END IF;
    ELSEIF IS_UUID(NEW.`TenantId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role.TenantId';
    ELSEIF LOWER(NEW.`TenantId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`TenantId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role.TenantId';
    ELSEIF NEW.`TenantIdBinary` IS NULL THEN
        SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
    ELSEIF NEW.`TenantIdBinary` <> UUID_TO_BIN(NEW.`TenantId`, 0) THEN
        IF (NEW.`TenantIdBinary` <=> OLD.`TenantIdBinary`) AND NOT (NEW.`TenantId` <=> OLD.`TenantId`) THEN
            SET NEW.`TenantIdBinary` = UUID_TO_BIN(NEW.`TenantId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role.TenantId';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_user_role_UuidBinary_BI`
BEFORE INSERT ON `fn_identity_user_role`
FOR EACH ROW
BEGIN
    IF NEW.`UserId` IS NULL THEN
        IF NEW.`UserIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user_role.UserId';
        END IF;
    ELSEIF IS_UUID(NEW.`UserId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user_role.UserId';
    ELSEIF LOWER(NEW.`UserId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`UserId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user_role.UserId';
    ELSEIF NEW.`UserIdBinary` IS NULL THEN
        SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
    ELSEIF NEW.`UserIdBinary` <> UUID_TO_BIN(NEW.`UserId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user_role.UserId';
    END IF;
    IF NEW.`RoleId` IS NULL THEN
        IF NEW.`RoleIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user_role.RoleId';
        END IF;
    ELSEIF IS_UUID(NEW.`RoleId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user_role.RoleId';
    ELSEIF LOWER(NEW.`RoleId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`RoleId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user_role.RoleId';
    ELSEIF NEW.`RoleIdBinary` IS NULL THEN
        SET NEW.`RoleIdBinary` = UUID_TO_BIN(NEW.`RoleId`, 0);
    ELSEIF NEW.`RoleIdBinary` <> UUID_TO_BIN(NEW.`RoleId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user_role.RoleId';
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_user_role_UuidBinary_BU`
BEFORE UPDATE ON `fn_identity_user_role`
FOR EACH ROW
BEGIN
    IF NEW.`UserId` IS NULL THEN
        IF NEW.`UserIdBinary` <=> OLD.`UserIdBinary` THEN
            SET NEW.`UserIdBinary` = NULL;
        ELSEIF NEW.`UserIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user_role.UserId';
        END IF;
    ELSEIF IS_UUID(NEW.`UserId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user_role.UserId';
    ELSEIF LOWER(NEW.`UserId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`UserId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user_role.UserId';
    ELSEIF NEW.`UserIdBinary` IS NULL THEN
        SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
    ELSEIF NEW.`UserIdBinary` <> UUID_TO_BIN(NEW.`UserId`, 0) THEN
        IF (NEW.`UserIdBinary` <=> OLD.`UserIdBinary`) AND NOT (NEW.`UserId` <=> OLD.`UserId`) THEN
            SET NEW.`UserIdBinary` = UUID_TO_BIN(NEW.`UserId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user_role.UserId';
        END IF;
    END IF;
    IF NEW.`RoleId` IS NULL THEN
        IF NEW.`RoleIdBinary` <=> OLD.`RoleIdBinary` THEN
            SET NEW.`RoleIdBinary` = NULL;
        ELSEIF NEW.`RoleIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user_role.RoleId';
        END IF;
    ELSEIF IS_UUID(NEW.`RoleId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user_role.RoleId';
    ELSEIF LOWER(NEW.`RoleId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`RoleId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_user_role.RoleId';
    ELSEIF NEW.`RoleIdBinary` IS NULL THEN
        SET NEW.`RoleIdBinary` = UUID_TO_BIN(NEW.`RoleId`, 0);
    ELSEIF NEW.`RoleIdBinary` <> UUID_TO_BIN(NEW.`RoleId`, 0) THEN
        IF (NEW.`RoleIdBinary` <=> OLD.`RoleIdBinary`) AND NOT (NEW.`RoleId` <=> OLD.`RoleId`) THEN
            SET NEW.`RoleIdBinary` = UUID_TO_BIN(NEW.`RoleId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_user_role.RoleId';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_role_permission_UuidBinary_BI`
BEFORE INSERT ON `fn_identity_role_permission`
FOR EACH ROW
BEGIN
    IF NEW.`RoleId` IS NULL THEN
        IF NEW.`RoleIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role_permission.RoleId';
        END IF;
    ELSEIF IS_UUID(NEW.`RoleId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role_permission.RoleId';
    ELSEIF LOWER(NEW.`RoleId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`RoleId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role_permission.RoleId';
    ELSEIF NEW.`RoleIdBinary` IS NULL THEN
        SET NEW.`RoleIdBinary` = UUID_TO_BIN(NEW.`RoleId`, 0);
    ELSEIF NEW.`RoleIdBinary` <> UUID_TO_BIN(NEW.`RoleId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role_permission.RoleId';
    END IF;
END$$

CREATE TRIGGER `TR_fn_identity_role_permission_UuidBinary_BU`
BEFORE UPDATE ON `fn_identity_role_permission`
FOR EACH ROW
BEGIN
    IF NEW.`RoleId` IS NULL THEN
        IF NEW.`RoleIdBinary` <=> OLD.`RoleIdBinary` THEN
            SET NEW.`RoleIdBinary` = NULL;
        ELSEIF NEW.`RoleIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role_permission.RoleId';
        END IF;
    ELSEIF IS_UUID(NEW.`RoleId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role_permission.RoleId';
    ELSEIF LOWER(NEW.`RoleId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`RoleId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_identity_role_permission.RoleId';
    ELSEIF NEW.`RoleIdBinary` IS NULL THEN
        SET NEW.`RoleIdBinary` = UUID_TO_BIN(NEW.`RoleId`, 0);
    ELSEIF NEW.`RoleIdBinary` <> UUID_TO_BIN(NEW.`RoleId`, 0) THEN
        IF (NEW.`RoleIdBinary` <=> OLD.`RoleIdBinary`) AND NOT (NEW.`RoleId` <=> OLD.`RoleId`) THEN
            SET NEW.`RoleIdBinary` = UUID_TO_BIN(NEW.`RoleId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_identity_role_permission.RoleId';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_seed_run_UuidBinary_BI`
BEFORE INSERT ON `fn_seed_run`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_seed_run.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_seed_run.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_seed_run.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_seed_run.Id';
    END IF;
END$$

CREATE TRIGGER `TR_fn_seed_run_UuidBinary_BU`
BEFORE UPDATE ON `fn_seed_run`
FOR EACH ROW
BEGIN
    IF NEW.`Id` IS NULL THEN
        IF NEW.`IdBinary` <=> OLD.`IdBinary` THEN
            SET NEW.`IdBinary` = NULL;
        ELSEIF NEW.`IdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_seed_run.Id';
        END IF;
    ELSEIF IS_UUID(NEW.`Id`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_seed_run.Id';
    ELSEIF LOWER(NEW.`Id`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`Id`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_seed_run.Id';
    ELSEIF NEW.`IdBinary` IS NULL THEN
        SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
    ELSEIF NEW.`IdBinary` <> UUID_TO_BIN(NEW.`Id`, 0) THEN
        IF (NEW.`IdBinary` <=> OLD.`IdBinary`) AND NOT (NEW.`Id` <=> OLD.`Id`) THEN
            SET NEW.`IdBinary` = UUID_TO_BIN(NEW.`Id`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_seed_run.Id';
        END IF;
    END IF;
END$$

CREATE TRIGGER `TR_fn_seed_run_item_UuidBinary_BI`
BEFORE INSERT ON `fn_seed_run_item`
FOR EACH ROW
BEGIN
    IF NEW.`RunId` IS NULL THEN
        IF NEW.`RunIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_seed_run_item.RunId';
        END IF;
    ELSEIF IS_UUID(NEW.`RunId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_seed_run_item.RunId';
    ELSEIF LOWER(NEW.`RunId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`RunId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_seed_run_item.RunId';
    ELSEIF NEW.`RunIdBinary` IS NULL THEN
        SET NEW.`RunIdBinary` = UUID_TO_BIN(NEW.`RunId`, 0);
    ELSEIF NEW.`RunIdBinary` <> UUID_TO_BIN(NEW.`RunId`, 0) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_seed_run_item.RunId';
    END IF;
END$$

CREATE TRIGGER `TR_fn_seed_run_item_UuidBinary_BU`
BEFORE UPDATE ON `fn_seed_run_item`
FOR EACH ROW
BEGIN
    IF NEW.`RunId` IS NULL THEN
        IF NEW.`RunIdBinary` <=> OLD.`RunIdBinary` THEN
            SET NEW.`RunIdBinary` = NULL;
        ELSEIF NEW.`RunIdBinary` IS NOT NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_seed_run_item.RunId';
        END IF;
    ELSEIF IS_UUID(NEW.`RunId`) = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_seed_run_item.RunId';
    ELSEIF LOWER(NEW.`RunId`) <> LOWER(BIN_TO_UUID(UUID_TO_BIN(NEW.`RunId`, 0), 0)) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Invalid UUID: fn_seed_run_item.RunId';
    ELSEIF NEW.`RunIdBinary` IS NULL THEN
        SET NEW.`RunIdBinary` = UUID_TO_BIN(NEW.`RunId`, 0);
    ELSEIF NEW.`RunIdBinary` <> UUID_TO_BIN(NEW.`RunId`, 0) THEN
        IF (NEW.`RunIdBinary` <=> OLD.`RunIdBinary`) AND NOT (NEW.`RunId` <=> OLD.`RunId`) THEN
            SET NEW.`RunIdBinary` = UUID_TO_BIN(NEW.`RunId`, 0);
        ELSE
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UUID shadow conflict: fn_seed_run_item.RunId';
        END IF;
    END IF;
END$$

DELIMITER ;

DROP TEMPORARY TABLE fn_uuid_expand_column;
