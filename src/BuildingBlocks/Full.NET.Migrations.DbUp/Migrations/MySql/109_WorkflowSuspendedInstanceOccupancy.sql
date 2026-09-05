-- 109：把实例业务唯一占用从仅运行中扩展到暂停中，避免暂停后同业务再启动、恢复时撞唯一约束。
-- MySQL 使用 STORED generated column 加 UNIQUE；InnoDB 隐式提交 DDL，因此必须先删唯一约束再改表达式，脚本可重复执行。
-- 不可逆风险：已存在“暂停实例 + 同业务新运行实例”脏数据时重建 UNIQUE 会失败，必须先清理冲突行。
SET @hasActiveBusinessKeyUnique := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_instance'
      AND INDEX_NAME = 'UX_fn_workflow_instance_ActiveBusinessKey');

SET @dropActiveBusinessKeyUnique := IF(
    @hasActiveBusinessKeyUnique > 0,
    'ALTER TABLE fn_workflow_instance DROP INDEX UX_fn_workflow_instance_ActiveBusinessKey',
    'SELECT 1');
PREPARE stmt FROM @dropActiveBusinessKeyUnique;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

ALTER TABLE fn_workflow_instance
    MODIFY COLUMN ActiveBusinessKey varchar(258) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
        GENERATED ALWAYS AS (
            CASE WHEN StatusKey IN ('active', 'suspended')
                THEN CONCAT(TenantScopeKey, '|', BusinessType, '|', BusinessId)
                ELSE NULL END) STORED COMMENT '占用中的实例业务唯一键';

SET @hasActiveBusinessKeyUnique := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_instance'
      AND INDEX_NAME = 'UX_fn_workflow_instance_ActiveBusinessKey');

SET @addActiveBusinessKeyUnique := IF(
    @hasActiveBusinessKeyUnique = 0,
    'ALTER TABLE fn_workflow_instance ADD CONSTRAINT UX_fn_workflow_instance_ActiveBusinessKey UNIQUE (ActiveBusinessKey)',
    'SELECT 1');
PREPARE stmt FROM @addActiveBusinessKeyUnique;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
