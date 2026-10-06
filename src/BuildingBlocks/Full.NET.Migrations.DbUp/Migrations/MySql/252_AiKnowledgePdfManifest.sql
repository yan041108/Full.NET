-- 252：共享任务主键保持文本/PDF 清单互斥，不重写载荷或回填旧任务。
-- MySQL DDL 隐式提交，每个中断点独立恢复；新格式/大小约束先启用，再移除旧文本守卫。
-- 需迁移窗口的元数据锁；不自动收缩已有 PDF 行，恢复采用前滚重放。
-- 格式按字节长度精确匹配；旧库非法格式导致校验失败，保留旧守卫并由显式修复处理。
SET @FnAiPdfManifestDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_text_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_text_manifest_Formats' AND CONSTRAINT_TYPE='CHECK'),
    'ALTER TABLE fn_ai_knowledge_text_manifest ADD CONSTRAINT CK_fn_ai_knowledge_text_manifest_Formats CHECK (((FormatKey=''knowledge-text-manifest-v1'' AND OCTET_LENGTH(FormatKey)=26) OR (FormatKey=''knowledge-pdf-manifest-v1'' AND OCTET_LENGTH(FormatKey)=25)) AND OCTET_LENGTH(Payload) BETWEEN 8 AND 131072)', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPdfManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_text_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_text_manifest_Formats' AND ENFORCED='NO'),
    'ALTER TABLE fn_ai_knowledge_text_manifest ALTER CHECK CK_fn_ai_knowledge_text_manifest_Formats ENFORCED', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPdfManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_text_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_text_manifest_Formats' AND ENFORCED='YES') AND EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_text_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_text_manifest_Bounds' AND CONSTRAINT_TYPE='CHECK'),
    'ALTER TABLE fn_ai_knowledge_text_manifest DROP CHECK CK_fn_ai_knowledge_text_manifest_Bounds', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPdfManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_text_manifest' AND TABLE_COMMENT<>'知识库索引任务不可替换的完整文本或PDF计划'),
    'ALTER TABLE fn_ai_knowledge_text_manifest COMMENT=''知识库索引任务不可替换的完整文本或PDF计划''', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPdfManifestDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_text_manifest' AND COLUMN_NAME='Payload' AND COLUMN_COMMENT<>'不含正文的完整有界文本或PDF批次清单'),
    'ALTER TABLE fn_ai_knowledge_text_manifest MODIFY COLUMN Payload mediumblob NOT NULL COMMENT ''不含正文的完整有界文本或PDF批次清单''', 'SELECT 1');
PREPARE stmt FROM @FnAiPdfManifestDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
