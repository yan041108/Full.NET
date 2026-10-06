-- 253：原批次主键/外键不变，扩展 PDF 向量格式，不重写载荷、账本或 ready。
-- MySQL DDL 隐式提交，元数据逐步探测恢复；新 CHECK 校验并启用后才移除旧文本守卫。
-- 二进制排序规则仍忽略尾空格，格式和 cosine 同时校验字节长度；非法旧行失败关闭，须显式修复后前滚。
-- 需迁移窗口的元数据锁；应用可回退读取文本，但有 PDF 行时不能自动收缩格式或删除数据。
SET @FnAiPdfVectorDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_Formats' AND CONSTRAINT_TYPE='CHECK'),
    'ALTER TABLE fn_ai_knowledge_index_vector ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_Formats CHECK (Dimensions IN (1536,3072) AND ((FormatKey=''knowledge-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=25) OR (FormatKey=''knowledge-pdf-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=29)) AND DistanceKey=''cosine'' AND OCTET_LENGTH(DistanceKey)=6 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 524288)', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPdfVectorDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_Formats' AND ENFORCED='NO'),
    'ALTER TABLE fn_ai_knowledge_index_vector ALTER CHECK CK_fn_ai_knowledge_index_vector_Formats ENFORCED', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPdfVectorDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_Formats' AND ENFORCED='YES') AND EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_Bounds' AND CONSTRAINT_TYPE='CHECK'),
    'ALTER TABLE fn_ai_knowledge_index_vector DROP CHECK CK_fn_ai_knowledge_index_vector_Bounds', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPdfVectorDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND TABLE_COMMENT<>'知识库已结算文本或PDF向量批次派生数据'),
    'ALTER TABLE fn_ai_knowledge_index_vector COMMENT=''知识库已结算文本或PDF向量批次派生数据''', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPdfVectorDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND COLUMN_NAME='Payload' AND COLUMN_COMMENT<>'完整有界文本或PDF分页片段与小端向量载荷'),
    'ALTER TABLE fn_ai_knowledge_index_vector MODIFY COLUMN Payload mediumblob NOT NULL COMMENT ''完整有界文本或PDF分页片段与小端向量载荷''', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfVectorDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
