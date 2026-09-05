-- 116：为包容网关复用汇合状态表，补充网关类型并放宽最少激活分支数。
SET @gateway_type_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_parallel_join'
      AND COLUMN_NAME = 'GatewayTypeKey');
SET @sql := IF(
    @gateway_type_exists = 0,
    'ALTER TABLE fn_workflow_parallel_join ADD COLUMN GatewayTypeKey varchar(16) NOT NULL DEFAULT ''parallel'' COMMENT ''网关类型键：parallel 或 inclusive'' AFTER JoinNodeKey',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

ALTER TABLE fn_workflow_parallel_join
    DROP CHECK CK_fn_workflow_parallel_join_RequiredBranchCount;
ALTER TABLE fn_workflow_parallel_join
    ADD CONSTRAINT CK_fn_workflow_parallel_join_RequiredBranchCount
        CHECK (RequiredBranchCount >= 1 AND RequiredBranchCount <= 8);

SET @gateway_type_check_exists := (
    SELECT COUNT(*)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_parallel_join'
      AND CONSTRAINT_NAME = 'CK_fn_workflow_parallel_join_GatewayTypeKey');
SET @sql := IF(
    @gateway_type_check_exists = 0,
    'ALTER TABLE fn_workflow_parallel_join ADD CONSTRAINT CK_fn_workflow_parallel_join_GatewayTypeKey CHECK (GatewayTypeKey IN (''parallel'', ''inclusive''))',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
