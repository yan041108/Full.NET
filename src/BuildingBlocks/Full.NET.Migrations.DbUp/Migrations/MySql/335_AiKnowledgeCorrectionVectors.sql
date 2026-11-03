-- 固定人工向量格式与既有账本/载荷边界共存；固定元数据探测不接受外部表名或 SQL。
SET @KnowledgeCorrectionVectorSql=IF(NOT EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_CorrectionFormats'),'ALTER TABLE fn_ai_knowledge_index_vector ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_CorrectionFormats CHECK (Dimensions IN (1536,3072) AND DistanceKey=''cosine'' AND OCTET_LENGTH(DistanceKey)=6 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 524288 AND ((FormatKey=''knowledge-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=25) OR (FormatKey=''knowledge-pdf-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=29) OR (FormatKey=''knowledge-docx-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=30) OR (FormatKey=''knowledge-document-vector-batch-v1'' AND OCTET_LENGTH(FormatKey)=34) OR (FormatKey=''knowledge-correction-vector-v1'' AND OCTET_LENGTH(FormatKey)=30)))','SELECT 1');
PREPARE KnowledgeCorrectionVectorStatement FROM @KnowledgeCorrectionVectorSql;
EXECUTE KnowledgeCorrectionVectorStatement;
DEALLOCATE PREPARE KnowledgeCorrectionVectorStatement;
SET @KnowledgeCorrectionVectorSql=IF(EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_CorrectionFormats' AND ENFORCED='NO'),'ALTER TABLE fn_ai_knowledge_index_vector ALTER CHECK CK_fn_ai_knowledge_index_vector_CorrectionFormats ENFORCED','SELECT 1');
PREPARE KnowledgeCorrectionVectorStatement FROM @KnowledgeCorrectionVectorSql;
EXECUTE KnowledgeCorrectionVectorStatement;
DEALLOCATE PREPARE KnowledgeCorrectionVectorStatement;
SET @KnowledgeCorrectionVectorSql=IF(EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_CorrectionFormats' AND ENFORCED='YES') AND EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_DocumentFormats'),'ALTER TABLE fn_ai_knowledge_index_vector DROP CHECK CK_fn_ai_knowledge_index_vector_DocumentFormats','SELECT 1');
PREPARE KnowledgeCorrectionVectorStatement FROM @KnowledgeCorrectionVectorSql;
EXECUTE KnowledgeCorrectionVectorStatement;
DEALLOCATE PREPARE KnowledgeCorrectionVectorStatement;
