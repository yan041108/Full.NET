-- 245：机构仅保存权威标识，不向 Organization 建立外键；当前应用继续支持旧用户名单。
-- 245：机构授予仅属于本模块知识库；可信租户与所有者通过父目录核对。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_org (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    KnowledgeBaseId binary(16) NOT NULL COMMENT '所属知识库标识',
    OrganizationUnitId binary(16) NOT NULL COMMENT '获授权机构标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_org PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_org_KnowledgeBase FOREIGN KEY (KnowledgeBaseId) REFERENCES fn_ai_knowledge_base (Id),
    CONSTRAINT UQ_fn_ai_knowledge_org_BaseUnit UNIQUE (KnowledgeBaseId, OrganizationUnitId)
) COMMENT='知识库机构授予' ENGINE=InnoDB;
-- 后置索引可独立恢复；唯一约束已随原子建表提交。
SET @org_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_org' AND INDEX_NAME = 'IX_fn_ai_knowledge_org_UnitBase');
SET @org_ddl := IF(@org_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_org_UnitBase ON fn_ai_knowledge_org (OrganizationUnitId, KnowledgeBaseId)', 'SELECT 1');
PREPARE stmt FROM @org_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 245：机构授予仅属于本模块知识库；可信租户与所有者通过父目录核对。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_document_org (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    DocumentId binary(16) NOT NULL COMMENT '所属文档标识',
    OrganizationUnitId binary(16) NOT NULL COMMENT '获授权机构标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_document_org PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_document_org_Document FOREIGN KEY (DocumentId) REFERENCES fn_ai_knowledge_document (Id),
    CONSTRAINT UQ_fn_ai_knowledge_document_org_DocUnit UNIQUE (DocumentId, OrganizationUnitId)
) COMMENT='文档机构授予' ENGINE=InnoDB;
-- 后置索引可独立恢复；唯一约束已随原子建表提交。
SET @org_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_document_org' AND INDEX_NAME = 'IX_fn_ai_knowledge_document_org_UnitDoc');
SET @org_ddl := IF(@org_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_document_org_UnitDoc ON fn_ai_knowledge_document_org (OrganizationUnitId, DocumentId)', 'SELECT 1');
PREPARE stmt FROM @org_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
