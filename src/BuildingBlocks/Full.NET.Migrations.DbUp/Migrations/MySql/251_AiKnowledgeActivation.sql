-- 251：每个未记账中断点独立恢复，新增约束始终先于移除旧约束。
DROP PROCEDURE IF EXISTS fn_ai_knowledge_activation_recover;
DELIMITER $$
CREATE PROCEDURE fn_ai_knowledge_activation_recover()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_task_Lifecycle' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_ai_knowledge_index_task ADD CONSTRAINT CK_fn_ai_knowledge_index_task_Lifecycle CHECK (StateKey IN ('queued','running','failed','cancelled','completed'));
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_index_task_State' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_ai_knowledge_index_task DROP CHECK CK_fn_ai_knowledge_index_task_State;
    END IF;
    CREATE TABLE IF NOT EXISTS fn_ai_knowledge_active_generation (
        Id binary(16) NOT NULL COMMENT '逻辑主键',
        DocumentId binary(16) NOT NULL COMMENT '唯一所属文档标识',
        IndexTaskId binary(16) NOT NULL COMMENT '唯一已完成索引任务标识',
        VersionId binary(16) NOT NULL COMMENT '来源版本标识',
        PlanHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整文本计划 SHA256 摘要',
        ManifestHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原始清单字节 SHA256 摘要',
        ReceiptHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整回执 JSON 字节 SHA256 摘要',
        ReceiptJson mediumtext CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '无正文的完整有界批次回执',
        LeaseToken binary(16) NOT NULL COMMENT '原完成租约标识，仅用于内部重试',
        ActivatedAtUtc datetime(6) NOT NULL COMMENT '激活时间（UTC）',
        CONSTRAINT PK_fn_ai_knowledge_active_generation PRIMARY KEY (Id),
        CONSTRAINT FK_fn_ai_knowledge_active_generation_DocumentId FOREIGN KEY (DocumentId) REFERENCES fn_ai_knowledge_document (Id),
        CONSTRAINT FK_fn_ai_knowledge_active_generation_IndexTaskId FOREIGN KEY (IndexTaskId) REFERENCES fn_ai_knowledge_index_task (Id),
        CONSTRAINT FK_fn_ai_knowledge_active_generation_VersionId FOREIGN KEY (VersionId) REFERENCES fn_ai_knowledge_version (Id),
        CONSTRAINT CK_fn_ai_knowledge_active_generation_Receipt CHECK (JSON_VALID(ReceiptJson)=1 AND OCTET_LENGTH(ReceiptJson) BETWEEN 2 AND 1048576)
    ) COMMENT='知识库文档当前完整活动代次及原完成回执' ENGINE=InnoDB;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND INDEX_NAME='UQ_fn_ai_knowledge_active_generation_DocumentId') THEN
        CREATE UNIQUE INDEX UQ_fn_ai_knowledge_active_generation_DocumentId ON fn_ai_knowledge_active_generation (DocumentId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND INDEX_NAME='UQ_fn_ai_knowledge_active_generation_IndexTaskId') THEN
        CREATE UNIQUE INDEX UQ_fn_ai_knowledge_active_generation_IndexTaskId ON fn_ai_knowledge_active_generation (IndexTaskId);
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='Id' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN Id binary(16) NOT NULL COMMENT '逻辑主键';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='DocumentId' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN DocumentId binary(16) NOT NULL COMMENT '唯一所属文档标识';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='IndexTaskId' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN IndexTaskId binary(16) NOT NULL COMMENT '唯一已完成索引任务标识';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='VersionId' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN VersionId binary(16) NOT NULL COMMENT '来源版本标识';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='PlanHash' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN PlanHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整文本计划 SHA256 摘要';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='ManifestHash' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN ManifestHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原始清单字节 SHA256 摘要';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='ReceiptHash' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN ReceiptHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整回执 JSON 字节 SHA256 摘要';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='ReceiptJson' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN ReceiptJson mediumtext CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '无正文的完整有界批次回执';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='LeaseToken' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN LeaseToken binary(16) NOT NULL COMMENT '原完成租约标识，仅用于内部重试';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_active_generation' AND COLUMN_NAME='ActivatedAtUtc' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_ai_knowledge_active_generation MODIFY COLUMN ActivatedAtUtc datetime(6) NOT NULL COMMENT '激活时间（UTC）';
    END IF;
END$$
DELIMITER ;
CALL fn_ai_knowledge_activation_recover();
DROP PROCEDURE IF EXISTS fn_ai_knowledge_activation_recover;
