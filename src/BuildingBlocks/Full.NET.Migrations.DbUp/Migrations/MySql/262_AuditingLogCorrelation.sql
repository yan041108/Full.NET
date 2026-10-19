-- 262：为 Auditing 日志增加关联快照；可空增量列，历史不回填，回退保留列。

SET @FnLogCorrelationDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_access_log' AND COLUMN_NAME='CorrelationJson'),
    'ALTER TABLE fn_auditing_access_log ADD COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_operation_log' AND COLUMN_NAME='CorrelationJson'),
    'ALTER TABLE fn_auditing_operation_log ADD COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_exception_log' AND COLUMN_NAME='CorrelationJson'),
    'ALTER TABLE fn_auditing_exception_log ADD COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_outbound_call' AND COLUMN_NAME='CorrelationJson'),
    'ALTER TABLE fn_auditing_outbound_call ADD COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_access_log' AND COLUMN_NAME='CorrelationJson' AND COLUMN_COMMENT=''),
    'ALTER TABLE fn_auditing_access_log MODIFY COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_operation_log' AND COLUMN_NAME='CorrelationJson' AND COLUMN_COMMENT=''),
    'ALTER TABLE fn_auditing_operation_log MODIFY COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_exception_log' AND COLUMN_NAME='CorrelationJson' AND COLUMN_COMMENT=''),
    'ALTER TABLE fn_auditing_exception_log MODIFY COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnLogCorrelationDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_outbound_call' AND COLUMN_NAME='CorrelationJson' AND COLUMN_COMMENT=''),
    'ALTER TABLE fn_auditing_outbound_call MODIFY COLUMN CorrelationJson varchar(2048) NULL COMMENT ''事件产生时的关联快照；历史缺失为空，不含凭据或原始IP''', 'SELECT 1');
PREPARE stmt FROM @FnLogCorrelationDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
