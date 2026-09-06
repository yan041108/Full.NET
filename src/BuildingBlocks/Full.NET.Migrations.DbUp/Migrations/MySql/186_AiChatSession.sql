-- 186：AI 聊天会话与消息表。

CREATE TABLE IF NOT EXISTS fn_ai_chat_session
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    TenantId char(36) COLLATE utf8mb4_bin NULL,
    OwnerUserId char(36) COLLATE utf8mb4_bin NOT NULL,
    ModelConfigId char(36) COLLATE utf8mb4_bin NOT NULL,
    ModelName varchar(128) NOT NULL,
    Title varchar(256) NOT NULL,
    MessageCount int NOT NULL DEFAULT 0,
    LastMessageAtUtc datetime(6) NULL,
    IsGenerating tinyint(1) NOT NULL DEFAULT 0,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_ai_chat_message
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    SessionId char(36) COLLATE utf8mb4_bin NOT NULL,
    RoleKey varchar(16) COLLATE utf8mb4_bin NOT NULL,
    Content longtext NOT NULL,
    StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL,
    PromptTokens int NULL,
    CompletionTokens int NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_chat_message_RoleKey
        CHECK (RoleKey IN ('user', 'assistant', 'system')),
    CONSTRAINT CK_fn_ai_chat_message_StatusKey
        CHECK (StatusKey IN ('streaming', 'completed', 'cancelled', 'failed'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_chat_session'
      AND INDEX_NAME = 'IX_fn_ai_chat_session_OwnerUserId_UpdatedAtUtc'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_ai_chat_session_OwnerUserId_UpdatedAtUtc ON fn_ai_chat_session (OwnerUserId, UpdatedAtUtc DESC, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_chat_message'
      AND INDEX_NAME = 'IX_fn_ai_chat_message_SessionId_CreatedAtUtc'
);
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_ai_chat_message_SessionId_CreatedAtUtc ON fn_ai_chat_message (SessionId, CreatedAtUtc, Id)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
