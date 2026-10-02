-- 242：MySQL DDL 隐式提交，建表与后置索引分别按实际结构幂等恢复。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_document (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    KnowledgeBaseId binary(16) NOT NULL COMMENT '所属知识库标识',
    Title varchar(200) NOT NULL COMMENT '受授权保护的文档标题',
    Description varchar(2000) NULL COMMENT '受授权保护的文档描述',
    IsDeleted boolean NOT NULL COMMENT '权威删除状态',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间（UTC）',
    Version int NOT NULL COMMENT '文档元数据与授权并发版本',
    CONSTRAINT PK_fn_ai_knowledge_document PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_document_KnowledgeBase FOREIGN KEY (KnowledgeBaseId) REFERENCES fn_ai_knowledge_base (Id),
    CONSTRAINT CK_fn_ai_knowledge_document_Version CHECK (Version > 0),
    CONSTRAINT CK_fn_ai_knowledge_document_IsDeleted CHECK (IsDeleted IN (0, 1))
) COMMENT='知识库文档草稿及权威删除状态' ENGINE=InnoDB;
SET @document_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_document' AND INDEX_NAME = 'IX_fn_ai_knowledge_document_BaseList');
SET @document_ddl := IF(@document_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_document_BaseList ON fn_ai_knowledge_document (KnowledgeBaseId, IsDeleted, CreatedAtUtc DESC, Id DESC)', 'SELECT 1');
PREPARE stmt FROM @document_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_document_member (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    DocumentId binary(16) NOT NULL COMMENT '所属文档标识',
    UserId binary(16) NOT NULL COMMENT '获授权用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_document_member PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_document_member_Document FOREIGN KEY (DocumentId) REFERENCES fn_ai_knowledge_document (Id),
    CONSTRAINT UQ_fn_ai_knowledge_document_member_DocUser UNIQUE (DocumentId, UserId)
) COMMENT='知识库文档独立明确用户授权' ENGINE=InnoDB;
SET @document_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_document_member' AND INDEX_NAME = 'IX_fn_ai_knowledge_document_member_UserDoc');
SET @document_ddl := IF(@document_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_document_member_UserDoc ON fn_ai_knowledge_document_member (UserId, DocumentId)', 'SELECT 1');
PREPARE stmt FROM @document_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
