-- FAQ 草稿与不可变修订；不创建文件、索引或发布，DDL 允许部分完成后未记账重跑。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_faq (
        Id binary(16) NOT NULL COMMENT 'FAQ UUID v7',
        KnowledgeBaseId binary(16) NOT NULL COMMENT '所属知识库',
        ReservedDocumentId binary(16) NOT NULL COMMENT '预留文档 UUID v7，草稿阶段不物化文档',
        TenantId binary(16) NULL COMMENT '可信租户；NULL 表示 Host',
        CreatedById binary(16) NOT NULL COMMENT '原始创建者',
        CreateRequestKey binary(16) NOT NULL COMMENT '创建幂等请求 UUID v7',
        CreateHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '创建请求完整摘要',
        OriginKey varchar(32) NOT NULL COMMENT 'independent 或 document_derived，不允许修改',
        Basis varchar(1024) NULL COMMENT '独立编写依据，派生内容必须为空',
        SourceJson mediumtext NOT NULL COMMENT '服务器冻结的完整文档来源协议',
        SourceHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '不可变原始来源摘要',
        HeadRevisionId binary(16) NOT NULL COMMENT '当前草稿修订身份',
        HeadVersion int NOT NULL COMMENT '草稿头乐观并发版本',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
        CONSTRAINT PK_fn_ai_knowledge_faq PRIMARY KEY (Id),
        CONSTRAINT CK_fn_ai_knowledge_faq_Bounds CHECK (HeadVersion>0 AND ((OriginKey='independent' AND Basis IS NOT NULL) OR (OriginKey='document_derived' AND Basis IS NULL))),
        UNIQUE INDEX UX_fn_ai_knowledge_faq_ReservedDocumentId (ReservedDocumentId),
        UNIQUE INDEX UX_fn_ai_knowledge_faq_KnowledgeBaseId_CreateRequestKey (KnowledgeBaseId,CreateRequestKey)
    ) COMMENT='标准问答草稿根，不可变原始来源与预留文档身份' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_faq_revision (
        Id binary(16) NOT NULL COMMENT '修订 UUID v7',
        FaqId binary(16) NOT NULL COMMENT '所属 FAQ',
        RevisionNo int NOT NULL COMMENT '从一开始的修订次序',
        RequestKey binary(16) NOT NULL COMMENT '本次修改幂等请求 UUID v7',
        RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '绑定操作与原头的完整请求摘要',
        ContentJson mediumtext NOT NULL COMMENT '冻结问题同义问法答案分类及有效期',
        ContentHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '绑定正文和原始来源的摘要',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '修订创建时间 UTC',
        RestoredFromRevisionId binary(16) NULL COMMENT '恢复时引用的本 FAQ 历史修订',
        CONSTRAINT PK_fn_ai_knowledge_faq_revision PRIMARY KEY (Id),
        CONSTRAINT CK_fn_ai_knowledge_faq_revision_Bounds CHECK (RevisionNo>0),
        UNIQUE INDEX UX_fn_ai_knowledge_faq_revision_FaqId_RequestKey (FaqId,RequestKey),
        UNIQUE INDEX UX_fn_ai_knowledge_faq_revision_FaqId_RevisionNo (FaqId,RevisionNo)
    ) COMMENT='标准问答不可变草稿修订；仅追加不更新' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
