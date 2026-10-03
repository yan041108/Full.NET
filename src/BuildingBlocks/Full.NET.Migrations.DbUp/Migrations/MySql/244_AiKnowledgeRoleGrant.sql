-- 244：角色仅保存权威标识，不向 Identity 建立外键；当前应用继续支持旧用户名单。
-- 244：角色授予仅属于本模块知识库；可信租户与所有者通过父目录核对。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_role (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    KnowledgeBaseId binary(16) NOT NULL COMMENT '所属知识库标识',
    RoleId binary(16) NOT NULL COMMENT '获授权角色标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_role PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_role_KnowledgeBase FOREIGN KEY (KnowledgeBaseId) REFERENCES fn_ai_knowledge_base (Id),
    CONSTRAINT UQ_fn_ai_knowledge_role_BaseRole UNIQUE (KnowledgeBaseId, RoleId)
) COMMENT='知识库角色授予' ENGINE=InnoDB;
-- 后置索引可独立恢复；唯一约束已随原子建表提交。
SET @role_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_role' AND INDEX_NAME = 'IX_fn_ai_knowledge_role_RoleBase');
SET @role_ddl := IF(@role_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_role_RoleBase ON fn_ai_knowledge_role (RoleId, KnowledgeBaseId)', 'SELECT 1');
PREPARE stmt FROM @role_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 244：角色授予仅属于本模块知识库；可信租户与所有者通过父目录核对。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_document_role (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    DocumentId binary(16) NOT NULL COMMENT '所属文档标识',
    RoleId binary(16) NOT NULL COMMENT '获授权角色标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_document_role PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_document_role_Document FOREIGN KEY (DocumentId) REFERENCES fn_ai_knowledge_document (Id),
    CONSTRAINT UQ_fn_ai_knowledge_document_role_DocRole UNIQUE (DocumentId, RoleId)
) COMMENT='文档角色授予' ENGINE=InnoDB;
-- 后置索引可独立恢复；唯一约束已随原子建表提交。
SET @role_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_document_role' AND INDEX_NAME = 'IX_fn_ai_knowledge_document_role_RoleDoc');
SET @role_ddl := IF(@role_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_document_role_RoleDoc ON fn_ai_knowledge_document_role (RoleId, DocumentId)', 'SELECT 1');
PREPARE stmt FROM @role_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
