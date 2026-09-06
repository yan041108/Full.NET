-- 177：Reporting 分组、定义与发布版本表。

CREATE TABLE IF NOT EXISTS fn_reporting_group
(
    Id binary(16) NOT NULL,
    ParentId binary(16) NULL,
    Name varchar(128) NOT NULL,
    SortOrder int NOT NULL DEFAULT 0,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_reporting_definition
(
    Id binary(16) NOT NULL,
    GroupId binary(16) NOT NULL,
    DataSourceId binary(16) NOT NULL,
    DefinitionKey varchar(128) COLLATE utf8mb4_bin NOT NULL,
    Name varchar(128) NOT NULL,
    Description varchar(512) NULL,
    QueryPortKey varchar(128) COLLATE utf8mb4_bin NOT NULL,
    ParameterSchemaJson longtext NOT NULL,
    LayoutConfigJson longtext NOT NULL,
    LatestPublishedVersionNumber int NOT NULL DEFAULT 0,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_reporting_definition_LatestPublishedVersionNumber CHECK (LatestPublishedVersionNumber >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_reporting_definition_version
(
    Id binary(16) NOT NULL,
    DefinitionId binary(16) NOT NULL,
    VersionNumber int NOT NULL,
    DataSourceId binary(16) NOT NULL,
    QueryPortKey varchar(128) COLLATE utf8mb4_bin NOT NULL,
    ParameterSchemaJson longtext NOT NULL,
    LayoutConfigJson longtext NOT NULL,
    ChangeNote varchar(512) NULL,
    PublishedByUserId binary(16) NOT NULL,
    PublishedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_reporting_definition_version_VersionNumber CHECK (VersionNumber > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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
