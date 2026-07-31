-- 036：职位可选绑定当前租户职级；应用层负责校验租户一致性与职级启用状态。
SET @hasPositionLevelId := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_organization_position'
      AND COLUMN_NAME = 'PositionLevelId');

SET @addPositionLevelId := IF(
    @hasPositionLevelId = 0,
    'ALTER TABLE fn_organization_position ADD COLUMN PositionLevelId BINARY(16) NULL AFTER UnitId',
    'SELECT 1');
PREPARE stmt FROM @addPositionLevelId;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasPositionLevelForeignKey := (
    SELECT COUNT(1)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_organization_position'
      AND CONSTRAINT_NAME = 'FK_fn_organization_position_PositionLevel'
      AND CONSTRAINT_TYPE = 'FOREIGN KEY');

SET @addPositionLevelForeignKey := IF(
    @hasPositionLevelForeignKey = 0,
    'ALTER TABLE fn_organization_position ADD CONSTRAINT FK_fn_organization_position_PositionLevel FOREIGN KEY (PositionLevelId) REFERENCES fn_organization_position_level(Id)',
    'SELECT 1');
PREPARE stmt FROM @addPositionLevelForeignKey;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasPositionLevelIndex := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_organization_position'
      AND INDEX_NAME = 'IX_fn_organization_position_Tenant_PositionLevel');

SET @addPositionLevelIndex := IF(
    @hasPositionLevelIndex = 0,
    'CREATE INDEX IX_fn_organization_position_Tenant_PositionLevel ON fn_organization_position(TenantId, PositionLevelId)',
    'SELECT 1');
PREPARE stmt FROM @addPositionLevelIndex;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
