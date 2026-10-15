-- 260：每项 DDL 独立探测，允许 MySQL 隐式提交后的未记账重放；不重置任务、账本或活动指针。
SET @FnAiRebuildDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND COLUMN_NAME='Generation'),
    'ALTER TABLE fn_ai_knowledge_index_task ADD COLUMN Generation int NOT NULL DEFAULT 0 COMMENT ''同一来源快照内的单调重建代次；既有任务为零''', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiRebuildDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND COLUMN_NAME='RebuildRequestId'),
    'ALTER TABLE fn_ai_knowledge_index_task ADD COLUMN RebuildRequestId BINARY(16) NULL COMMENT ''文档内幂等重建请求键；不是任务主键''', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiRebuildDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND COLUMN_NAME='RebuildPreviousTaskId'),
    'ALTER TABLE fn_ai_knowledge_index_task ADD COLUMN RebuildPreviousTaskId BINARY(16) NULL COMMENT ''显式重建时预期的上一任务；保留原任务与费用历史''', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiRebuildDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_task_Generation'),
    'ALTER TABLE fn_ai_knowledge_index_task ADD CONSTRAINT CK_fn_ai_knowledge_index_task_Generation CHECK ((Generation=0 AND RebuildRequestId IS NULL AND RebuildPreviousTaskId IS NULL) OR (Generation>0 AND RebuildRequestId IS NOT NULL AND RebuildPreviousTaskId IS NOT NULL AND RebuildPreviousTaskId<>Id))', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
-- 保持原来源唯一约束直至新约束和请求幂等索引均已建立。
SET @FnAiRebuildDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_task_Generation' AND ENFORCED='NO'),
    'ALTER TABLE fn_ai_knowledge_index_task ALTER CHECK CK_fn_ai_knowledge_index_task_Generation ENFORCED', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiRebuildDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND INDEX_NAME='UQ_fn_ai_knowledge_index_task_SourceGeneration'),
    'ALTER TABLE fn_ai_knowledge_index_task ADD CONSTRAINT UQ_fn_ai_knowledge_index_task_SourceGeneration UNIQUE (VersionId,DocumentVersion,KnowledgeBaseVersion,Generation)', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
-- 列已建立但说明或默认值未完成时，恢复结构声明，不重写任务及费用历史。
SET @FnAiRebuildDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND COLUMN_NAME='Generation' AND (COLUMN_COMMENT<>'同一来源快照内的单调重建代次；既有任务为零' OR COLUMN_DEFAULT IS NULL OR COLUMN_DEFAULT<>'0')),
    'ALTER TABLE fn_ai_knowledge_index_task MODIFY COLUMN Generation int NOT NULL DEFAULT 0 COMMENT ''同一来源快照内的单调重建代次；既有任务为零''', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiRebuildDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND COLUMN_NAME='RebuildRequestId' AND COLUMN_COMMENT<>'文档内幂等重建请求键；不是任务主键'),
    'ALTER TABLE fn_ai_knowledge_index_task MODIFY COLUMN RebuildRequestId BINARY(16) NULL COMMENT ''文档内幂等重建请求键；不是任务主键''', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiRebuildDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND COLUMN_NAME='RebuildPreviousTaskId' AND COLUMN_COMMENT<>'显式重建时预期的上一任务；保留原任务与费用历史'),
    'ALTER TABLE fn_ai_knowledge_index_task MODIFY COLUMN RebuildPreviousTaskId BINARY(16) NULL COMMENT ''显式重建时预期的上一任务；保留原任务与费用历史''', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiRebuildDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND INDEX_NAME='UX_fn_ai_knowledge_index_task_RebuildRequest'),
    'CREATE UNIQUE INDEX UX_fn_ai_knowledge_index_task_RebuildRequest ON fn_ai_knowledge_index_task (DocumentId,RebuildRequestId)', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiRebuildDdl := IF(EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND INDEX_NAME='UQ_fn_ai_knowledge_index_task_Source'),
    'ALTER TABLE fn_ai_knowledge_index_task DROP INDEX UQ_fn_ai_knowledge_index_task_Source', 'SELECT 1');
PREPARE stmt FROM @FnAiRebuildDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
