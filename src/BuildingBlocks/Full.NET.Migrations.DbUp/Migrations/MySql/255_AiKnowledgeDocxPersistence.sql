-- 255：InnoDB 原子创建任务清单及同模块外键，重放不覆盖清单；旧任务不回填，不推进 ready。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_docx_manifest (
    Id binary(16) NOT NULL COMMENT '逻辑主键及所属索引任务标识',
    FormatKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定清单格式版本',
    PayloadHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整清单载荷 SHA256 摘要',
    Payload mediumblob NOT NULL COMMENT '包含完整正文及逻辑块坐标的有界 DOCX 计划',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_docx_manifest PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_docx_manifest_Id FOREIGN KEY (Id) REFERENCES fn_ai_knowledge_index_task (Id),
    CONSTRAINT CK_fn_ai_knowledge_docx_manifest_Bounds CHECK (FormatKey='knowledge-docx-manifest-v1' AND OCTET_LENGTH(FormatKey)=26 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 8388608)
) COMMENT='知识库索引任务不可替换的完整 DOCX 计划' ENGINE=InnoDB;

SET @FnAiDocxManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_docx_manifest' AND COLUMN_NAME='Id' AND COLUMN_COMMENT<>'逻辑主键及所属索引任务标识'), 'ALTER TABLE fn_ai_knowledge_docx_manifest MODIFY COLUMN Id binary(16) NOT NULL COMMENT ''逻辑主键及所属索引任务标识''', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnAiDocxManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_docx_manifest' AND COLUMN_NAME='FormatKey' AND COLUMN_COMMENT<>'固定清单格式版本'), 'ALTER TABLE fn_ai_knowledge_docx_manifest MODIFY COLUMN FormatKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''固定清单格式版本''', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnAiDocxManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_docx_manifest' AND COLUMN_NAME='PayloadHash' AND COLUMN_COMMENT<>'完整清单载荷 SHA256 摘要'), 'ALTER TABLE fn_ai_knowledge_docx_manifest MODIFY COLUMN PayloadHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''完整清单载荷 SHA256 摘要''', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnAiDocxManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_docx_manifest' AND COLUMN_NAME='Payload' AND COLUMN_COMMENT<>'包含完整正文及逻辑块坐标的有界 DOCX 计划'), 'ALTER TABLE fn_ai_knowledge_docx_manifest MODIFY COLUMN Payload mediumblob NOT NULL COMMENT ''包含完整正文及逻辑块坐标的有界 DOCX 计划''', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @FnAiDocxManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_docx_manifest' AND COLUMN_NAME='CreatedAtUtc' AND COLUMN_COMMENT<>'创建时间（UTC）'), 'ALTER TABLE fn_ai_knowledge_docx_manifest MODIFY COLUMN CreatedAtUtc datetime(6) NOT NULL COMMENT ''创建时间（UTC）''', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 255：原批次主键/外键不变，扩展 DOCX 向量格式，不重写载荷、账本或 ready。
-- MySQL DDL 隐式提交，元数据逐步探测恢复；新 CHECK 校验并启用后才移除旧文本/PDF守卫。
-- 二进制排序规则仍忽略尾空格，格式和 cosine 同时校验字节长度；非法旧行失败关闭，须显式修复后前滚。
-- 需迁移窗口的元数据锁；应用可回退读取文本，但有 DOCX 行时不能自动收缩格式或删除数据。
SET @FnAiDocxVectorDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_DocxFormats' AND CONSTRAINT_TYPE='CHECK'),
    'ALTER TABLE fn_ai_knowledge_index_vector ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_DocxFormats CHECK (Dimensions IN (1536,3072) AND ((FormatKey=''knowledge-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=25) OR (FormatKey=''knowledge-pdf-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=29) OR (FormatKey=''knowledge-docx-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=30)) AND DistanceKey=''cosine'' AND OCTET_LENGTH(DistanceKey)=6 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 524288)', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiDocxVectorDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_DocxFormats' AND ENFORCED='NO'),
    'ALTER TABLE fn_ai_knowledge_index_vector ALTER CHECK CK_fn_ai_knowledge_index_vector_DocxFormats ENFORCED', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiDocxVectorDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_DocxFormats' AND ENFORCED='YES') AND EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_Formats' AND CONSTRAINT_TYPE='CHECK'),
    'ALTER TABLE fn_ai_knowledge_index_vector DROP CHECK CK_fn_ai_knowledge_index_vector_Formats', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiDocxVectorDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND TABLE_COMMENT<>'知识库已结算文本、PDF或DOCX向量批次派生数据'),
    'ALTER TABLE fn_ai_knowledge_index_vector COMMENT=''知识库已结算文本、PDF或DOCX向量批次派生数据''', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiDocxVectorDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND COLUMN_NAME='Payload' AND COLUMN_COMMENT<>'有界文本片段、PDF分页或DOCX逻辑块及小端向量载荷'),
    'ALTER TABLE fn_ai_knowledge_index_vector MODIFY COLUMN Payload mediumblob NOT NULL COMMENT ''有界文本片段、PDF分页或DOCX逻辑块及小端向量载荷''', 'SELECT 1');
PREPARE stmt FROM @FnAiDocxVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
