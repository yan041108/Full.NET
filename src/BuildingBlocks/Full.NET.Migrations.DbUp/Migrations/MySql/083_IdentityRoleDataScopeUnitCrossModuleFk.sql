-- 083 移除 Identity 数据范围表指向 Organization 的跨模块外键；引用完整性由应用层校验承担。
SET @drop_scope_unit_unit_fk := IF(
    EXISTS(
        SELECT 1
        FROM information_schema.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_role_data_scope_unit'
          AND CONSTRAINT_NAME = 'FK_fn_identity_role_data_scope_unit_Unit'),
    'ALTER TABLE fn_identity_role_data_scope_unit DROP FOREIGN KEY FK_fn_identity_role_data_scope_unit_Unit',
    'SELECT 1');
PREPARE stmt FROM @drop_scope_unit_unit_fk;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;