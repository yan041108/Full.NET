-- 119：DataApproval 场景目录绑定表。
CREATE TABLE IF NOT EXISTS fn_dataapproval_scenario (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；Host 级为 NULL',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '租户作用域键',
    ScenarioKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '场景键',
    IsEnabled tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否启用',
    WorkflowDefinitionKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '工作流定义键',
    WorkflowDefinitionVersionId BINARY(16) NULL COMMENT '绑定的已发布工作流定义版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_dataapproval_scenario PRIMARY KEY (Id),
    CONSTRAINT CK_fn_dataapproval_scenario_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_dataapproval_scenario_Version CHECK (Version > 0),
    UNIQUE KEY UX_fn_dataapproval_scenario_ScenarioKey (TenantScopeKey, ScenarioKey)
) COMMENT='数据审批场景绑定表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @scenario_index_exists := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_dataapproval_scenario'
      AND INDEX_NAME = 'UX_fn_dataapproval_scenario_ScenarioKey');
SET @ddl := IF(
    @scenario_index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_dataapproval_scenario_ScenarioKey ON fn_dataapproval_scenario (TenantScopeKey, ScenarioKey)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
