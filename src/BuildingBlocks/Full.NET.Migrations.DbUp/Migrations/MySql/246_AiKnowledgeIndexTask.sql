-- 246：只追加索引任务；InnoDB 原子建表保留唯一键与状态约束。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_index_task (
    Id binary(16) NOT NULL COMMENT '逻辑主键及索引任务代次',
    DocumentId binary(16) NOT NULL COMMENT '所属文档标识',
    VersionId binary(16) NOT NULL COMMENT '不可变来源文件版本标识',
    DocumentVersion int NOT NULL COMMENT '预期文档并发版本',
    KnowledgeBaseVersion int NOT NULL COMMENT '预期目录与授权版本',
    ModelConfigId binary(16) NOT NULL COMMENT '获批向量模型配置标识',
    ModelConfigVersion int NOT NULL COMMENT '获批精确模型配置版本',
    StateKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '持久化索引阶段',
    AttemptCount int NOT NULL COMMENT '当前人工重试周期内领取次数',
    FailureCode varchar(160) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '稳定失败码',
    LeaseToken binary(16) NULL COMMENT '当前执行者租约令牌',
    LeaseExpiresAtUtc datetime(6) NULL COMMENT '租约过期时间（UTC）',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_index_task PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_index_task_Document FOREIGN KEY (DocumentId) REFERENCES fn_ai_knowledge_document (Id),
    CONSTRAINT FK_fn_ai_knowledge_index_task_Version FOREIGN KEY (VersionId) REFERENCES fn_ai_knowledge_version (Id),
    CONSTRAINT UQ_fn_ai_knowledge_index_task_Source UNIQUE (VersionId, DocumentVersion, KnowledgeBaseVersion),
    CONSTRAINT CK_fn_ai_knowledge_index_task_Versions CHECK (DocumentVersion > 0 AND KnowledgeBaseVersion > 0 AND ModelConfigVersion > 0),
    CONSTRAINT CK_fn_ai_knowledge_index_task_Attempt CHECK (AttemptCount >= 0 AND AttemptCount <= 3),
    CONSTRAINT CK_fn_ai_knowledge_index_task_State CHECK (StateKey IN ('queued','running','failed','cancelled')),
    CONSTRAINT CK_fn_ai_knowledge_index_task_Lease CHECK ((StateKey = 'running' AND LeaseToken IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL) OR (StateKey <> 'running' AND LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL))
) COMMENT='知识库索引任务及有界执行租约' ENGINE=InnoDB;
-- 后置索引独立恢复，不改写已存在的任务或重试次数。
SET @index_task_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_index_task' AND INDEX_NAME = 'IX_fn_ai_knowledge_index_task_StateTime');
SET @index_task_ddl := IF(@index_task_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_index_task_StateTime ON fn_ai_knowledge_index_task (StateKey, LeaseExpiresAtUtc, CreatedAtUtc, Id)', 'SELECT 1');
PREPARE stmt FROM @index_task_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
