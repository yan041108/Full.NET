-- 277：Notifications 投递发送治理快照列。

SET @schema_name := DATABASE();

SET @sql := IF(
    EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema_name AND TABLE_NAME = 'fn_notifications_delivery' AND COLUMN_NAME = 'ContentSnapshotJson'),
    'SELECT 1',
    'ALTER TABLE fn_notifications_delivery ADD ContentSnapshotJson longtext NULL COMMENT ''按收件人冻结的内容快照(JSON)''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(
    EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema_name AND TABLE_NAME = 'fn_notifications_delivery' AND COLUMN_NAME = 'PreferenceVersion'),
    'SELECT 1',
    'ALTER TABLE fn_notifications_delivery ADD PreferenceVersion bigint NULL COMMENT ''发送决策读取的偏好版本''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(
    EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema_name AND TABLE_NAME = 'fn_notifications_delivery' AND COLUMN_NAME = 'PolicyDecisionKey'),
    'SELECT 1',
    'ALTER TABLE fn_notifications_delivery ADD PolicyDecisionKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''发送政策决策键''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(
    EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema_name AND TABLE_NAME = 'fn_notifications_delivery' AND COLUMN_NAME = 'SuppressionReasonCode'),
    'SELECT 1',
    'ALTER TABLE fn_notifications_delivery ADD SuppressionReasonCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT ''抑制原因码''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
