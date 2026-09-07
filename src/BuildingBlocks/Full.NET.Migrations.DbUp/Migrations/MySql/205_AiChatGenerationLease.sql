-- 205：生成槽位持久化所有权、到期回收和远程取消；发布时停止旧 API 实例。

SET @generation_ddl := IF(NOT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'GenerationId'),
    'ALTER TABLE fn_ai_chat_session ADD COLUMN GenerationId binary(16) NULL COMMENT ''当前生成所有权标识''', 'SELECT 1');
PREPARE generation_stmt FROM @generation_ddl;
EXECUTE generation_stmt;
DEALLOCATE PREPARE generation_stmt;

SET @generation_ddl := IF(NOT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'GenerationExpiresAtUtc'),
    'ALTER TABLE fn_ai_chat_session ADD COLUMN GenerationExpiresAtUtc datetime(6) NULL COMMENT ''生成租约到期时间 UTC''', 'SELECT 1');
PREPARE generation_stmt FROM @generation_ddl;
EXECUTE generation_stmt;
DEALLOCATE PREPARE generation_stmt;

SET @generation_ddl := IF(NOT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_chat_session' AND COLUMN_NAME = 'GenerationCancellationRequested'),
    'ALTER TABLE fn_ai_chat_session ADD COLUMN GenerationCancellationRequested boolean NOT NULL DEFAULT 0 COMMENT ''跨实例取消请求标志''', 'SELECT 1');
PREPARE generation_stmt FROM @generation_ddl;
EXECUTE generation_stmt;
DEALLOCATE PREPARE generation_stmt;

-- 只释放旧版本留下且没有生成标识的状态；重跑时保留所有新版有效租约。
UPDATE fn_ai_chat_message SET StatusKey = 'failed' WHERE StatusKey = 'streaming' AND EXISTS (SELECT 1 FROM fn_ai_chat_session WHERE fn_ai_chat_session.Id = fn_ai_chat_message.SessionId AND GenerationId IS NULL AND IsGenerating = 1);
UPDATE fn_ai_chat_session SET IsGenerating = 0 WHERE IsGenerating = 1 AND GenerationId IS NULL;
