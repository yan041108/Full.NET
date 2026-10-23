-- 277：MySQL DDL 隐式提交；静态结构探测逐项收敛，不重写存量载荷或费用。
DROP PROCEDURE IF EXISTS fn_ai_knowledge_document_parsing_recover;
DELIMITER $$
CREATE PROCEDURE fn_ai_knowledge_document_parsing_recover()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest') AND EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_docx_manifest') THEN
        RENAME TABLE fn_ai_knowledge_docx_manifest TO fn_ai_knowledge_document_manifest;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_import' AND COLUMN_NAME='ParsingPolicyKey') THEN
        ALTER TABLE fn_ai_knowledge_import ADD COLUMN ParsingPolicyKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'default' COMMENT '导入时冻结的解析策略键，参与重试身份与索引计划摘要';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_import' AND COLUMN_NAME='ParsingPolicyKey' AND COLUMN_COMMENT<>'导入时冻结的解析策略键，参与重试身份与索引计划摘要') THEN
        ALTER TABLE fn_ai_knowledge_import MODIFY COLUMN ParsingPolicyKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'default' COMMENT '导入时冻结的解析策略键，参与重试身份与索引计划摘要';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_version' AND COLUMN_NAME='ParsingPolicyKey') THEN
        ALTER TABLE fn_ai_knowledge_version ADD COLUMN ParsingPolicyKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'default' COMMENT '导入时冻结的解析策略键，参与重试身份与索引计划摘要';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_version' AND COLUMN_NAME='ParsingPolicyKey' AND COLUMN_COMMENT<>'导入时冻结的解析策略键，参与重试身份与索引计划摘要') THEN
        ALTER TABLE fn_ai_knowledge_version MODIFY COLUMN ParsingPolicyKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'default' COMMENT '导入时冻结的解析策略键，参与重试身份与索引计划摘要';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND COLUMN_NAME='FormatKey' AND CHARACTER_MAXIMUM_LENGTH<64) THEN
        ALTER TABLE fn_ai_knowledge_index_vector MODIFY COLUMN FormatKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定向量格式版本';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_document_manifest_Bounds' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_ai_knowledge_document_manifest ADD CONSTRAINT CK_fn_ai_knowledge_document_manifest_Bounds CHECK (((FormatKey='knowledge-document-manifest-v1' AND OCTET_LENGTH(FormatKey)=30 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 16777215) OR (FormatKey='knowledge-docx-manifest-v1' AND OCTET_LENGTH(FormatKey)=26 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 8388608)));
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_document_manifest_Bounds' AND ENFORCED='NO') THEN
        ALTER TABLE fn_ai_knowledge_document_manifest ALTER CHECK CK_fn_ai_knowledge_document_manifest_Bounds ENFORCED;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_document_manifest_Bounds' AND ENFORCED='YES') AND EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_docx_manifest_Bounds') THEN
        ALTER TABLE fn_ai_knowledge_document_manifest DROP CHECK CK_fn_ai_knowledge_docx_manifest_Bounds;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_DocumentFormats' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_ai_knowledge_index_vector ADD CONSTRAINT CK_fn_ai_knowledge_index_vector_DocumentFormats CHECK (Dimensions IN (1536,3072) AND ((FormatKey='knowledge-vector-batch-v1' AND OCTET_LENGTH(FormatKey)=25) OR (FormatKey='knowledge-pdf-vector-batch-v1' AND OCTET_LENGTH(FormatKey)=29) OR (FormatKey='knowledge-docx-vector-batch-v1' AND OCTET_LENGTH(FormatKey)=30) OR (FormatKey='knowledge-document-vector-batch-v1' AND OCTET_LENGTH(FormatKey)=34)) AND DistanceKey='cosine' AND OCTET_LENGTH(DistanceKey)=6 AND OCTET_LENGTH(Payload) BETWEEN 8 AND 524288);
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_DocumentFormats' AND ENFORCED='NO') THEN
        ALTER TABLE fn_ai_knowledge_index_vector ALTER CHECK CK_fn_ai_knowledge_index_vector_DocumentFormats ENFORCED;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_DocumentFormats' AND ENFORCED='YES') AND EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_vector_DocxFormats') THEN
        ALTER TABLE fn_ai_knowledge_index_vector DROP CHECK CK_fn_ai_knowledge_index_vector_DocxFormats;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='FK_fn_ai_knowledge_document_manifest_Id' AND CONSTRAINT_TYPE='FOREIGN KEY') THEN
        ALTER TABLE fn_ai_knowledge_document_manifest ADD CONSTRAINT FK_fn_ai_knowledge_document_manifest_Id FOREIGN KEY (Id) REFERENCES fn_ai_knowledge_index_task (Id);
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND CONSTRAINT_NAME='FK_fn_ai_knowledge_docx_manifest_Id' AND CONSTRAINT_TYPE='FOREIGN KEY') THEN
        ALTER TABLE fn_ai_knowledge_document_manifest DROP FOREIGN KEY FK_fn_ai_knowledge_docx_manifest_Id;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND TABLE_COMMENT<>'知识库索引任务不可替换的完整多格式解析计划') THEN
        ALTER TABLE fn_ai_knowledge_document_manifest COMMENT '知识库索引任务不可替换的完整多格式解析计划';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_document_manifest' AND COLUMN_NAME='Payload' AND COLUMN_COMMENT<>'包含完整解析策略、正文及原生来源坐标的有界计划') THEN
        ALTER TABLE fn_ai_knowledge_document_manifest MODIFY COLUMN Payload mediumblob NOT NULL COMMENT '包含完整解析策略、正文及原生来源坐标的有界计划';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND TABLE_COMMENT<>'知识库已结算文本及多格式向量批次派生数据') THEN
        ALTER TABLE fn_ai_knowledge_index_vector COMMENT '知识库已结算文本及多格式向量批次派生数据';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND COLUMN_NAME='Payload' AND COLUMN_COMMENT<>'由完整清单绑定的有界来源区间及小端向量载荷') THEN
        ALTER TABLE fn_ai_knowledge_index_vector MODIFY COLUMN Payload mediumblob NOT NULL COMMENT '由完整清单绑定的有界来源区间及小端向量载荷';
    END IF;
END$$
DELIMITER ;
CALL fn_ai_knowledge_document_parsing_recover();
DROP PROCEDURE IF EXISTS fn_ai_knowledge_document_parsing_recover;
