-- 201：新增完整载荷摘要。历史未保存正文，不能伪造回填摘要；旧幂等键重放将失败关闭。
SET @column_exists := (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_mqtt_message' AND COLUMN_NAME = 'PayloadDigest'
);
SET @ddl := IF(@column_exists = 0,
    'ALTER TABLE fn_mqtt_message ADD PayloadDigest varchar(64) NULL COMMENT ''原 UTF-8 正文 SHA-256 摘要；历史记录为空''', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
