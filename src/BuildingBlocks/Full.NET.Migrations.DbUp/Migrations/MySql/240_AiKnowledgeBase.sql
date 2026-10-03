-- 240：MySQL DDL 隐式提交，目录与后置索引分别按结构重放。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_base (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    TenantId binary(16) NULL COMMENT '所属租户标识',
    OwnerUserId binary(16) NOT NULL COMMENT '知识库所有者标识',
    Name varchar(200) NOT NULL COMMENT '知识库名称',
    Description varchar(2000) NULL COMMENT '知识库描述',
    IsEnabled boolean NOT NULL COMMENT '是否启用',
    DataClassification varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '数据分类机器码',
    EmbeddingModelConfigId binary(16) NULL COMMENT '获批向量模型配置标识',
    EmbeddingModelVersion int NULL COMMENT '获批向量模型配置版本',
    GenerationModelConfigId binary(16) NULL COMMENT '获批生成模型配置标识',
    GenerationModelVersion int NULL COMMENT '获批生成模型配置版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL COMMENT '目录与审批乐观并发版本号',
    CONSTRAINT PK_fn_ai_knowledge_base PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_knowledge_base_Classification CHECK (DataClassification IN ('public', 'internal', 'restricted')),
    CONSTRAINT CK_fn_ai_knowledge_base_EmbeddingApproval CHECK ((EmbeddingModelConfigId IS NULL AND EmbeddingModelVersion IS NULL) OR (EmbeddingModelConfigId IS NOT NULL AND EmbeddingModelVersion IS NOT NULL AND EmbeddingModelVersion > 0)),
    CONSTRAINT CK_fn_ai_knowledge_base_GenerationApproval CHECK ((GenerationModelConfigId IS NULL AND GenerationModelVersion IS NULL) OR (GenerationModelConfigId IS NOT NULL AND GenerationModelVersion IS NOT NULL AND GenerationModelVersion > 0)),
    CONSTRAINT CK_fn_ai_knowledge_base_Version CHECK (Version > 0)
) COMMENT='私有知识库目录及模型处理审批' ENGINE=InnoDB;
SET @knowledge_index_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_base' AND INDEX_NAME = 'IX_fn_ai_knowledge_base_OwnerScope');
SET @knowledge_ddl := IF(@knowledge_index_exists = 0, 'CREATE INDEX IX_fn_ai_knowledge_base_OwnerScope ON fn_ai_knowledge_base (TenantId, OwnerUserId, CreatedAtUtc DESC, Id DESC)', 'SELECT 1');
PREPARE stmt FROM @knowledge_ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
