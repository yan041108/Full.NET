-- 254：为答案消息保存完整来源依赖和不可降级的派生标记。旧消息保持普通聊天语义。
-- MySQL DDL 可逐条提交，重跑补齐列、注释和有界守卫；不删除数据。开放问答前必须停止旧 API。
SET @answer_ddl := IF(NOT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_chat_message' AND COLUMN_NAME='IsKnowledgeAnswer'),
    'ALTER TABLE fn_ai_chat_message ADD COLUMN IsKnowledgeAnswer boolean NOT NULL DEFAULT 0 COMMENT ''是否携带知识来源依赖''', 'SELECT 1');
PREPARE answer_stmt FROM @answer_ddl;
EXECUTE answer_stmt;
DEALLOCATE PREPARE answer_stmt;
SET @answer_ddl := IF(NOT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_chat_message' AND COLUMN_NAME='KnowledgeLineage'),
    'ALTER TABLE fn_ai_chat_message ADD COLUMN KnowledgeLineage longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT ''绑定答案消息及可信范围的版本化完整来源依赖''', 'SELECT 1');
PREPARE answer_stmt FROM @answer_ddl;
EXECUTE answer_stmt;
DEALLOCATE PREPARE answer_stmt;
ALTER TABLE fn_ai_chat_message MODIFY COLUMN IsKnowledgeAnswer boolean NOT NULL DEFAULT 0 COMMENT '是否携带知识来源依赖';
ALTER TABLE fn_ai_chat_message MODIFY COLUMN KnowledgeLineage longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '绑定答案消息及可信范围的版本化完整来源依赖';

-- 元数据使用 UTF-8 字节上限；空依赖占位必须由读取边界拒绝释放正文。
SET @answer_ddl := IF(NOT EXISTS(SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_chat_message' AND CONSTRAINT_NAME='CK_fn_ai_chat_message_KnowledgeLineage'),
    'ALTER TABLE fn_ai_chat_message ADD CONSTRAINT CK_fn_ai_chat_message_KnowledgeLineage CHECK ((IsKnowledgeAnswer=0 AND KnowledgeLineage IS NULL) OR (IsKnowledgeAnswer=1 AND (KnowledgeLineage IS NULL OR OCTET_LENGTH(KnowledgeLineage) BETWEEN 1 AND 16384)))', 'SELECT 1');
PREPARE answer_stmt FROM @answer_ddl;
EXECUTE answer_stmt;
DEALLOCATE PREPARE answer_stmt;
ALTER TABLE fn_ai_chat_message ALTER CHECK CK_fn_ai_chat_message_KnowledgeLineage ENFORCED;
