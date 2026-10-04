-- 247：InnoDB 原子创建批次身份和约束；不保存正文、凭据或向量，不建立跨模块关系。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_index_batch (
    Id binary(16) NOT NULL COMMENT '逻辑主键及预算操作标识',
    IndexTaskId binary(16) NOT NULL COMMENT '所属索引任务代次',
    BatchOrdinal int NOT NULL COMMENT '任务内零起始批次号',
    RequestHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整预算请求指纹',
    InputCount int NOT NULL COMMENT '批次输入数量',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_index_batch PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_index_batch_IndexTaskId FOREIGN KEY (IndexTaskId) REFERENCES fn_ai_knowledge_index_task (Id),
    CONSTRAINT UX_fn_ai_knowledge_index_batch_IndexTaskId_BatchOrdinal UNIQUE (IndexTaskId, BatchOrdinal),
    CONSTRAINT CK_fn_ai_knowledge_index_batch_Bounds CHECK (BatchOrdinal >= 0 AND BatchOrdinal <= 4095 AND InputCount >= 1 AND InputCount <= 16)
) COMMENT='知识库索引批次与稳定预算操作身份' ENGINE=InnoDB;
-- 后置唯一索引只选择固定语句恢复；冲突数据保持失败，不清理或改写操作身份。
SET @index_batch_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_index_batch' AND INDEX_NAME = 'UX_fn_ai_knowledge_index_batch_IndexTaskId_BatchOrdinal');
SET @index_batch_ddl := IF(@index_batch_exists = 0, 'CREATE UNIQUE INDEX UX_fn_ai_knowledge_index_batch_IndexTaskId_BatchOrdinal ON fn_ai_knowledge_index_batch (IndexTaskId, BatchOrdinal)', 'SELECT 1');
PREPARE stmt FROM @index_batch_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
