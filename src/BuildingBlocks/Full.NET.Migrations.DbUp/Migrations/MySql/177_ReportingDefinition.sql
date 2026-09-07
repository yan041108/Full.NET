-- 177：Reporting 分组、定义与发布版本表。

CREATE TABLE IF NOT EXISTS fn_reporting_group (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    ParentId binary(16) NULL COMMENT '父级标识',
    Name varchar(128) NOT NULL COMMENT '名称',
    SortOrder int NOT NULL DEFAULT 0 COMMENT '排序顺序',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id)
) COMMENT='报表分组表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_reporting_definition (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    GroupId binary(16) NOT NULL COMMENT '分组标识',
    DataSourceId binary(16) NOT NULL COMMENT '数据源标识',
    DefinitionKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '定义稳定键',
    Name varchar(128) NOT NULL COMMENT '名称',
    Description varchar(512) NULL COMMENT '描述',
    QueryPortKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '查询端口键',
    ParameterSchemaJson longtext NOT NULL COMMENT '参数结构(JSON)',
    LayoutConfigJson longtext NOT NULL COMMENT 'Layout Config(JSON)',
    LatestPublishedVersionNumber int NOT NULL DEFAULT 0 COMMENT '最新已发布版本号',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_reporting_definition_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
) COMMENT='报表定义表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_reporting_definition_version (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    DefinitionId binary(16) NOT NULL COMMENT '定义标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    DataSourceId binary(16) NOT NULL COMMENT '数据源标识',
    QueryPortKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '查询端口键',
    ParameterSchemaJson longtext NOT NULL COMMENT '参数结构(JSON)',
    LayoutConfigJson longtext NOT NULL COMMENT 'Layout Config(JSON)',
    ChangeNote varchar(512) NULL COMMENT '变更说明',
    PublishedByUserId binary(16) NOT NULL COMMENT '发布人用户标识',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '发布时间(UTC)',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_reporting_definition_version_VersionNumber CHECK (VersionNumber > 0)
) COMMENT='报表定义版本表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_group'
      AND INDEX_NAME = 'IX_fn_reporting_group_ParentId_SortOrder');
SET @ddl := IF(@index_exists = 0,
    'CREATE INDEX IX_fn_reporting_group_ParentId_SortOrder ON fn_reporting_group (ParentId, SortOrder, Name, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_definition'
      AND INDEX_NAME = 'UX_fn_reporting_definition_DefinitionKey');
SET @ddl := IF(@index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_reporting_definition_DefinitionKey ON fn_reporting_definition (DefinitionKey)',
    'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_definition'
      AND INDEX_NAME = 'IX_fn_reporting_definition_GroupId_Name');
SET @ddl := IF(@index_exists = 0,
    'CREATE INDEX IX_fn_reporting_definition_GroupId_Name ON fn_reporting_definition (GroupId, Name, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_reporting_definition_version'
      AND INDEX_NAME = 'UX_fn_reporting_definition_version_DefinitionId_VersionNumber');
SET @ddl := IF(@index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_reporting_definition_version_DefinitionId_VersionNumber ON fn_reporting_definition_version (DefinitionId, VersionNumber)',
    'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
