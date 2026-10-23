-- 282：重试预算独立于单调递增的租约世代，治理延迟不消耗预算。
SET @schema_name := DATABASE();
SET @sql := IF(
    EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema_name AND TABLE_NAME = 'fn_notifications_delivery' AND COLUMN_NAME = 'AttemptBudgetUsed'),
    'SELECT 1',
    'ALTER TABLE fn_notifications_delivery ADD AttemptBudgetUsed bigint NULL COMMENT ''已消耗的自动尝试预算''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 可重跑回填：历史领取无法证明未执行，保守计入预算。
UPDATE fn_notifications_delivery
SET AttemptBudgetUsed = CASE WHEN LeaseGeneration > 1 THEN LeaseGeneration - 1 ELSE 0 END
WHERE AttemptBudgetUsed IS NULL;

ALTER TABLE fn_notifications_delivery
    MODIFY AttemptBudgetUsed bigint NOT NULL DEFAULT 0 COMMENT '已消耗的自动尝试预算';
