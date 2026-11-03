-- FAQ 显式数据库修订来源，不创建 Files 对象或伪造普通导入；各 DDL 可重复恢复。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_faq_source (
        Id binary(16) NOT NULL COMMENT '索引知识版本 UUID v7',
        FaqId binary(16) NOT NULL COMMENT '所属 FAQ',
        RevisionId binary(16) NOT NULL COMMENT '准确不可变 FAQ 修订',
        RevisionHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '修订正文及原始来源摘要',
        SourceHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '不可变完整来源摘要',
        DataClassification varchar(32) NOT NULL COMMENT '当前来源交集最严格数据分类',
        KnowledgeBaseVersion int NOT NULL COMMENT '首次索引批准的知识库版本',
        ModelConfigId binary(16) NOT NULL COMMENT '首次索引批准的模型身份',
        ModelConfigVersion int NOT NULL COMMENT '首次索引批准的模型版本',
        ModelFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '包含目的地和受保护凭据的完整模型摘要',
        BodyText mediumtext NOT NULL COMMENT '服务器确定性生成的修订索引文本',
        BodyHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'UTF-8 索引文本摘要',
        SizeBytes bigint NOT NULL COMMENT '索引文本 UTF-8 字节数',
        RequestKey binary(16) NOT NULL COMMENT '索引准入幂等请求 UUID v7',
        RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '冻结修订头和明确收费确认的请求摘要',
        ActorUserId binary(16) NOT NULL COMMENT '首次请求可信主体',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
        CONSTRAINT PK_fn_ai_knowledge_faq_source PRIMARY KEY (Id),
        UNIQUE INDEX UX_fn_ai_knowledge_faq_source_RevisionId (RevisionId),
        UNIQUE INDEX UX_fn_ai_knowledge_faq_source_FaqId_RequestKey (FaqId,RequestKey)
    ) COMMENT='FAQ 不可变修订的数据库索引来源，无文件或普通导入凭证' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;


CREATE TABLE IF NOT EXISTS fn_ai_knowledge_faq_release (
        Id binary(16) NOT NULL COMMENT '同一 AI 发布身份 UUID v7',
        FaqId binary(16) NOT NULL COMMENT '所属 FAQ',
        RevisionId binary(16) NOT NULL COMMENT '冻结审核修订',
        VersionId binary(16) NOT NULL COMMENT '冻结完整索引知识版本',
        SourceHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整原始来源摘要',
        ContentHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '审核修订内容摘要',
        RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '发布申请完整请求摘要',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
        CONSTRAINT PK_fn_ai_knowledge_faq_release PRIMARY KEY (Id)
    ) COMMENT='FAQ 审核发布绑定与普通发布协议隔离，指针原子推进' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_faq_query (
        Id binary(16) NOT NULL COMMENT '问法占用 UUID v7',
        KnowledgeBaseId binary(16) NOT NULL COMMENT '所属知识库',
        FaqId binary(16) NOT NULL COMMENT '拥有当前问法的 FAQ',
        QueryHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '版本一规范化完整问法摘要',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '占用创建时间 UTC',
        CONSTRAINT PK_fn_ai_knowledge_faq_query PRIMARY KEY (Id),
        UNIQUE INDEX UX_fn_ai_knowledge_faq_query_KnowledgeBaseId_QueryHash (KnowledgeBaseId,QueryHash)
    ) COMMENT='当前发布 FAQ 问法的库内唯一占用；撤销或替换在同一事务释放' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;


ALTER TABLE fn_ai_knowledge_version MODIFY FileId binary(16) NULL COMMENT '真实 Files 引用；FAQ 数据库修订时为空';




-- 固定恢复过程仅探测已知对象，不接受或拼接运行时 SQL。
DROP PROCEDURE IF EXISTS fn_ai_knowledge_faq_source_ensure;
DELIMITER $$
CREATE PROCEDURE fn_ai_knowledge_faq_source_ensure()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='fn_ai_knowledge_faq_source' AND index_name='UX_fn_ai_knowledge_faq_source_RevisionId') THEN
        CREATE UNIQUE INDEX UX_fn_ai_knowledge_faq_source_RevisionId ON fn_ai_knowledge_faq_source (RevisionId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='fn_ai_knowledge_faq_source' AND index_name='UX_fn_ai_knowledge_faq_source_FaqId_RequestKey') THEN
        CREATE UNIQUE INDEX UX_fn_ai_knowledge_faq_source_FaqId_RequestKey ON fn_ai_knowledge_faq_source (FaqId,RequestKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='fn_ai_knowledge_faq_query' AND index_name='UX_fn_ai_knowledge_faq_query_KnowledgeBaseId_QueryHash') THEN
        CREATE UNIQUE INDEX UX_fn_ai_knowledge_faq_query_KnowledgeBaseId_QueryHash ON fn_ai_knowledge_faq_query (KnowledgeBaseId,QueryHash);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='fn_ai_knowledge_version' AND column_name='FaqRevisionId') THEN
        ALTER TABLE fn_ai_knowledge_version ADD FaqRevisionId binary(16) NULL COMMENT 'FAQ 修订来源；与 FileId 二选一';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE table_schema=DATABASE() AND table_name='fn_ai_knowledge_version' AND constraint_name='CK_fn_ai_knowledge_version_SourceKind') THEN
        ALTER TABLE fn_ai_knowledge_version ADD CONSTRAINT CK_fn_ai_knowledge_version_SourceKind CHECK ((FileId IS NOT NULL AND FaqRevisionId IS NULL) OR (FileId IS NULL AND FaqRevisionId IS NOT NULL));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='fn_ai_knowledge_faq' AND column_name='PublicationId') THEN
        ALTER TABLE fn_ai_knowledge_faq ADD PublicationId binary(16) NULL COMMENT '当前发布身份；与正式发布头同事务推进';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE table_schema=DATABASE() AND table_name='fn_ai_knowledge_faq_source' AND constraint_name='CK_fn_ai_knowledge_faq_source_Bounds') THEN
        ALTER TABLE fn_ai_knowledge_faq_source ADD CONSTRAINT CK_fn_ai_knowledge_faq_source_Bounds CHECK (SizeBytes BETWEEN 1 AND 131072 AND OCTET_LENGTH(BodyText)<=131072 AND KnowledgeBaseVersion>0 AND ModelConfigVersion>0 AND DataClassification IN ('public','internal','restricted'));
    END IF;
END$$
DELIMITER ;
CALL fn_ai_knowledge_faq_source_ensure();
DROP PROCEDURE IF EXISTS fn_ai_knowledge_faq_source_ensure;
