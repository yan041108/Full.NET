-- 只展开可空租约字段，历史 processing 由 Worker 按原开始时间有界收口，不回填或重排任务。
-- MySQL DDL 隐式提交；每列及索引分别探测，部分完成未记账时可前向恢复。
SET @exists := (SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'LeaseId');
SET @ddl := IF(@exists = 0,
    'ALTER TABLE fn_document_preview_task ADD COLUMN LeaseId binary(16) NULL COMMENT ''本次处理租约身份''', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND COLUMN_NAME = 'LeaseExpiresAtUtc');
SET @ddl := IF(@exists = 0,
    'ALTER TABLE fn_document_preview_task ADD COLUMN LeaseExpiresAtUtc datetime(6) NULL COMMENT ''处理租约到期时间(UTC)''', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_preview_task' AND INDEX_NAME = 'IX_fn_document_preview_task_StatusKey_LeaseExpiresAtUtc');
SET @ddl := IF(@exists = 0,
    'CREATE INDEX IX_fn_document_preview_task_StatusKey_LeaseExpiresAtUtc ON fn_document_preview_task (StatusKey, LeaseExpiresAtUtc, StartedAtUtc, CreatedAtUtc, Id)', 'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
