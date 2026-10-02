-- 241：明确成员仅属于本模块知识库；可信租户与所有者通过父目录核对。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_member (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    KnowledgeBaseId binary(16) NOT NULL COMMENT '所属知识库标识',
    UserId binary(16) NOT NULL COMMENT '获授权用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_member PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_member_KnowledgeBase FOREIGN KEY (KnowledgeBaseId) REFERENCES fn_ai_knowledge_base (Id),
    CONSTRAINT UQ_fn_ai_knowledge_member_BaseUser UNIQUE (KnowledgeBaseId, UserId)
) COMMENT='知识库明确成员授权' ENGINE=InnoDB;
-- 后置索引可独立恢复；唯一约束已随原子建表提交。
SET @member_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_member' AND INDEX_NAME = 'IX_fn_ai_knowledge_member_UserBase');
SET @member_ddl := IF(@member_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_member_UserBase ON fn_ai_knowledge_member (UserId, KnowledgeBaseId)', 'SELECT 1');
PREPARE stmt FROM @member_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
