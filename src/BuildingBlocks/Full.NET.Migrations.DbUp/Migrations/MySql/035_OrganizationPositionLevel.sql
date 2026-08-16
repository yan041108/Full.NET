-- 035：租户职级目录。
CREATE TABLE IF NOT EXISTS fn_organization_position_level (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识；NULL 表示 Host 级',
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '编码',
    Name varchar(128) NOT NULL COMMENT '名称',
    DisplayOrder int NOT NULL COMMENT '显示顺序',
    IsActive boolean NOT NULL DEFAULT true COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_organization_position_level PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_organization_position_level_Tenant_Code (TenantId, Code),
    KEY IX_fn_organization_position_level_Tenant_DisplayOrder (TenantId, DisplayOrder, Code)
) COMMENT='组织机构职级表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @hasPositionLevelCodeIndex := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_organization_position_level'
      AND INDEX_NAME = 'UX_fn_organization_position_level_Tenant_Code');

SET @addPositionLevelCodeIndex := IF(
    @hasPositionLevelCodeIndex = 0,
    'CREATE UNIQUE INDEX UX_fn_organization_position_level_Tenant_Code ON fn_organization_position_level(TenantId, Code)',
    'SELECT 1');
PREPARE stmt FROM @addPositionLevelCodeIndex;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasPositionLevelDisplayIndex := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_organization_position_level'
      AND INDEX_NAME = 'IX_fn_organization_position_level_Tenant_DisplayOrder');

SET @addPositionLevelDisplayIndex := IF(
    @hasPositionLevelDisplayIndex = 0,
    'CREATE INDEX IX_fn_organization_position_level_Tenant_DisplayOrder ON fn_organization_position_level(TenantId, DisplayOrder, Code)',
    'SELECT 1');
PREPARE stmt FROM @addPositionLevelDisplayIndex;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
