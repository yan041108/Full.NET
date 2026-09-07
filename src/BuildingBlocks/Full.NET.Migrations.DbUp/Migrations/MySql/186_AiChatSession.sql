-- 186：AI 聊天会话与消息表。

CREATE TABLE IF NOT EXISTS fn_ai_chat_session (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    TenantId char(36) COLLATE utf8mb4_bin NULL COMMENT '租户标识；NULL 表示 Host 级',
    OwnerUserId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '所有者用户标识',
    ModelConfigId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '模型配置标识',
    ModelName varchar(128) NOT NULL COMMENT '模型名称',
    Title varchar(256) NOT NULL COMMENT '标题',
    MessageCount int NOT NULL DEFAULT 0 COMMENT '消息数量',
    LastMessageAtUtc datetime(6) NULL COMMENT 'Last Message At(UTC)',
    IsGenerating tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否正在生成',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id)
) COMMENT='人工智能对话会话表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_ai_chat_message (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    SessionId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '会话标识',
    RoleKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '角色键',
    Content longtext NOT NULL COMMENT '内容',
    StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '状态键',
    PromptTokens int NULL COMMENT '提示 Token 数',
    CompletionTokens int NULL COMMENT '补全 Token 数',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_chat_message_RoleKey
        CHECK (RoleKey IN ('user', 'assistant', 'system')),
    CONSTRAINT CK_fn_ai_chat_message_StatusKey
        CHECK (StatusKey IN ('streaming', 'completed', 'cancelled', 'failed'))
) COMMENT='人工智能对话消息表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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
