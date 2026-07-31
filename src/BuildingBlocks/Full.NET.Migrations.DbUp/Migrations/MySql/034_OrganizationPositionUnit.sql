-- 034：职位可选绑定当前租户机构；应用层负责校验租户一致性与机构启用状态。
SET @hasUnitId := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_organization_position'
      AND COLUMN_NAME = 'UnitId');

SET @addUnitId := IF(
    @hasUnitId = 0,
    'ALTER TABLE fn_organization_position ADD COLUMN UnitId BINARY(16) NULL AFTER Name',
    'SELECT 1');
PREPARE stmt FROM @addUnitId;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasUnitForeignKey := (
    SELECT COUNT(1)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_organization_position'
      AND CONSTRAINT_NAME = 'FK_fn_organization_position_Unit'
      AND CONSTRAINT_TYPE = 'FOREIGN KEY');

SET @addUnitForeignKey := IF(
    @hasUnitForeignKey = 0,
    'ALTER TABLE fn_organization_position ADD CONSTRAINT FK_fn_organization_position_Unit FOREIGN KEY (UnitId) REFERENCES fn_organization_unit(Id)',
    'SELECT 1');
PREPARE stmt FROM @addUnitForeignKey;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasUnitIndex := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_organization_position'
      AND INDEX_NAME = 'IX_fn_organization_position_Tenant_Unit');

SET @addUnitIndex := IF(
    @hasUnitIndex = 0,
    'CREATE INDEX IX_fn_organization_position_Tenant_Unit ON fn_organization_position(TenantId, UnitId)',
    'SELECT 1');
PREPARE stmt FROM @addUnitIndex;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
