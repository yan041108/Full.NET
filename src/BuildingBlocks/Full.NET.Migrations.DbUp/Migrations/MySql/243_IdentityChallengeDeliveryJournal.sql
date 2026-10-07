-- 按 ChallengeId 保存独立投递结果，不复制凭据或载荷。MySQL DDL 隐式提交，各列单独探测恢复。
-- 保障要求全部消费实例升级；混跑旧消费代码或回退前必须停止挑战入口并排空有效新挑战。
SET @DeliveryColumnExists = (SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_account_challenge' AND COLUMN_NAME = 'DeliveryStateKey');
SET @DeliveryColumnSql = IF(@DeliveryColumnExists = 0,
    'ALTER TABLE fn_identity_account_challenge ADD COLUMN DeliveryStateKey VARCHAR(16) NULL COMMENT ''投递结果机器码；空值仅表示迁移前旧挑战''', 'SELECT 1');
PREPARE ChallengeDeliveryColumn FROM @DeliveryColumnSql;
EXECUTE ChallengeDeliveryColumn;
DEALLOCATE PREPARE ChallengeDeliveryColumn;

SET @DeliveryColumnExists = (SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_account_challenge' AND COLUMN_NAME = 'DeliveryCompletedAtUtc');
SET @DeliveryColumnSql = IF(@DeliveryColumnExists = 0,
    'ALTER TABLE fn_identity_account_challenge ADD COLUMN DeliveryCompletedAtUtc DATETIME(6) NULL COMMENT ''投递结果持久化时间(UTC)''', 'SELECT 1');
PREPARE ChallengeDeliveryColumn FROM @DeliveryColumnSql;
EXECUTE ChallengeDeliveryColumn;
DEALLOCATE PREPARE ChallengeDeliveryColumn;

SET @DeliveryColumnExists = (SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_account_challenge' AND COLUMN_NAME = 'DeliveryReconciledAtUtc');
SET @DeliveryColumnSql = IF(@DeliveryColumnExists = 0,
    'ALTER TABLE fn_identity_account_challenge ADD COLUMN DeliveryReconciledAtUtc DATETIME(6) NULL COMMENT ''未确认投递撤销对账时间(UTC)''', 'SELECT 1');
PREPARE ChallengeDeliveryColumn FROM @DeliveryColumnSql;
EXECUTE ChallengeDeliveryColumn;
DEALLOCATE PREPARE ChallengeDeliveryColumn;
