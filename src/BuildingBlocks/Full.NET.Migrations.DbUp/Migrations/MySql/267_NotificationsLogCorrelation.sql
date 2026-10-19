-- 267：为 Notifications 日志增加关联快照；可空增量列，历史不回填，回退保留列。

SET @FnLogCorrelationDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_notifications_domain_audit' AND COLUMN_NAME='CorrelationJson'),
    'ALTER TABLE fn_notifications_domain_audit ADD COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_notifications_delivery_attempt' AND COLUMN_NAME='CorrelationJson'),
    'ALTER TABLE fn_notifications_delivery_attempt ADD COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_notifications_domain_audit' AND COLUMN_NAME='CorrelationJson' AND COLUMN_COMMENT=''),
    'ALTER TABLE fn_notifications_domain_audit MODIFY COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_notifications_delivery_attempt' AND COLUMN_NAME='CorrelationJson' AND COLUMN_COMMENT=''),
    'ALTER TABLE fn_notifications_delivery_attempt MODIFY COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
