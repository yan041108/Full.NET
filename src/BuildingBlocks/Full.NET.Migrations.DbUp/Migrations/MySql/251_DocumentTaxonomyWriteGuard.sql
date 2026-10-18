-- 251：分类与标签目录事务门禁。只增加技术行，不清洗、重命名或删除历史文档。
CREATE TABLE IF NOT EXISTS fn_document_taxonomy_write_guard
(
    Id BINARY(16) NOT NULL COMMENT '应用生成的固定 UUID v7 技术行标识',
    TenantId BINARY(16) NULL COMMENT '仅 Host 技术行，固定为 NULL',
    CONSTRAINT PK_fn_document_taxonomy_write_guard PRIMARY KEY (Id)
) COMMENT='Host 分类与标签目录事务门禁' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- MySQL DDL 会隐式提交；每一后续阶段必须能在未记账时重入。
SET @guardCheckExists := (SELECT COUNT(1) FROM information_schema.table_constraints
    WHERE constraint_schema = DATABASE() AND table_name = 'fn_document_taxonomy_write_guard'
      AND constraint_name = 'CK_fn_document_taxonomy_write_guard_HostScope' AND constraint_type = 'CHECK');
SET @guardDdl := IF(@guardCheckExists = 0,
    'ALTER TABLE fn_document_taxonomy_write_guard ADD CONSTRAINT CK_fn_document_taxonomy_write_guard_HostScope CHECK (TenantId IS NULL)', 'SELECT 1');
PREPARE guardStatement FROM @guardDdl;
EXECUTE guardStatement;
DEALLOCATE PREPARE guardStatement;

INSERT INTO fn_document_taxonomy_write_guard (Id, TenantId)
SELECT UNHEX('01a12ef8d3607d8991f67e58ddbfcd73'), NULL
WHERE NOT EXISTS (SELECT 1 FROM fn_document_taxonomy_write_guard WHERE Id = UNHEX('01a12ef8d3607d8991f67e58ddbfcd73'));


INSERT INTO fn_document_taxonomy_write_guard (Id, TenantId)
SELECT UNHEX('01a12ef8d36c78d9b24b93aa39be0747'), NULL
WHERE NOT EXISTS (SELECT 1 FROM fn_document_taxonomy_write_guard WHERE Id = UNHEX('01a12ef8d36c78d9b24b93aa39be0747'));
