-- 完整人工清单沿用现有 mediumblob 上限；超限拒绝完整计划，不截断来源或改变索引状态。
-- 固定字面量 DDL 分别恢复新增、启用和旧守卫移除；只有新约束已生效才可移除旧约束。
SET @KnowledgeCorrectionManifestSql=IF(NOT EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_document_manifest_Corrections'),'ALTER TABLE fn_ai_knowledge_document_manifest ADD CONSTRAINT CK_fn_ai_knowledge_document_manifest_Corrections CHECK ((FormatKey=''knowledge-document-manifest-v1'' AND OCTET_LENGTH(FormatKey)=30 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 16777215) OR (FormatKey=''knowledge-docx-manifest-v1'' AND OCTET_LENGTH(FormatKey)=26 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 8388608) OR (FormatKey=''knowledge-correction-manifest-v1'' AND OCTET_LENGTH(FormatKey)=32 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 16777215))','SELECT 1');
PREPARE KnowledgeCorrectionManifestStatement FROM @KnowledgeCorrectionManifestSql;
EXECUTE KnowledgeCorrectionManifestStatement;
DEALLOCATE PREPARE KnowledgeCorrectionManifestStatement;
SET @KnowledgeCorrectionManifestSql=IF(EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_document_manifest_Corrections' AND ENFORCED='NO'),'ALTER TABLE fn_ai_knowledge_document_manifest ALTER CHECK CK_fn_ai_knowledge_document_manifest_Corrections ENFORCED','SELECT 1');
PREPARE KnowledgeCorrectionManifestStatement FROM @KnowledgeCorrectionManifestSql;
EXECUTE KnowledgeCorrectionManifestStatement;
DEALLOCATE PREPARE KnowledgeCorrectionManifestStatement;
SET @KnowledgeCorrectionManifestSql=IF(EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_document_manifest_Corrections' AND ENFORCED='YES') AND EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_document_manifest_Bounds'),'ALTER TABLE fn_ai_knowledge_document_manifest DROP CHECK CK_fn_ai_knowledge_document_manifest_Bounds','SELECT 1');
PREPARE KnowledgeCorrectionManifestStatement FROM @KnowledgeCorrectionManifestSql;
EXECUTE KnowledgeCorrectionManifestStatement;
DEALLOCATE PREPARE KnowledgeCorrectionManifestStatement;
