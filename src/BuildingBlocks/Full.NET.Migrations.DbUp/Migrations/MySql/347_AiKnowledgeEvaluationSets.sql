-- 不可变人工脱敏评估集；仅新增 AI 自有表，不创建文件、调用模型或发布知识。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_evaluation_set (
        Id binary(16) NOT NULL COMMENT '评估集 UUID v7',
        KnowledgeBaseId binary(16) NOT NULL COMMENT '所属知识库',
        TenantId binary(16) NULL COMMENT '可信租户；NULL 表示 Host',
        CreatedById binary(16) NOT NULL COMMENT '原始创建者',
        RequestKey binary(16) NOT NULL COMMENT '创建幂等请求 UUID v7',
        CreateHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '创建请求与操作者完整摘要',
        HeadRevisionId binary(16) NOT NULL COMMENT '当前不可变修订',
        HeadRevisionNo int NOT NULL COMMENT '当前修订序号',
        Version int NOT NULL COMMENT '根乐观并发版本，批准也递增',
        FootprintJson mediumtext NOT NULL COMMENT '累计完整来源与正文贡献者，禁止清空',
        FootprintHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '累计来源规范 JSON 摘要',
        ApprovalId binary(16) NULL COMMENT '当前修订最近独立审核记录',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
        CONSTRAINT PK_fn_ai_knowledge_evaluation_set PRIMARY KEY (Id),
        CONSTRAINT CK_fn_ai_knowledge_evaluation_set_Bounds CHECK (HeadRevisionNo>0 AND Version>0),
        UNIQUE INDEX UX_fn_ai_knowledge_evaluation_set_KnowledgeBaseId_RequestKey (KnowledgeBaseId,RequestKey)
    ) COMMENT='知识库评估集根，当前头与累计不可降级来源' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_evaluation_revision (
        Id binary(16) NOT NULL COMMENT '修订 UUID v7',
        SetId binary(16) NOT NULL COMMENT '所属评估集',
        RevisionNo int NOT NULL COMMENT '从一开始的修订次序',
        RequestKey binary(16) NOT NULL COMMENT '本次追加的幂等请求 UUID v7',
        RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '绑定原头和操作者的请求摘要',
        ContentJson mediumtext NOT NULL COMMENT '人工脱敏正文及显式草稿或冻结状态',
        ContentHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '正文与该修订来源摘要',
        SourceJson mediumtext NOT NULL COMMENT '该修订不可变累计来源和贡献者足迹',
        SourceHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '该修订来源摘要',
        EditorUserId binary(16) NOT NULL COMMENT '该修订编辑者，不能批准自身正文',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '修订创建时间 UTC',
        RestoredFromRevisionId binary(16) NULL COMMENT '明确恢复的本评估集历史修订',
        CONSTRAINT PK_fn_ai_knowledge_evaluation_revision PRIMARY KEY (Id),
        CONSTRAINT CK_fn_ai_knowledge_evaluation_revision_Bounds CHECK (RevisionNo>0),
        UNIQUE INDEX UX_fn_ai_knowledge_evaluation_revision_SetId_RequestKey (SetId,RequestKey),
        UNIQUE INDEX UX_fn_ai_knowledge_evaluation_revision_SetId_RevisionNo (SetId,RevisionNo)
    ) COMMENT='人工脱敏评估不可变修订，仅追加不覆盖' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_evaluation_approval (
        Id binary(16) NOT NULL COMMENT '审核 UUID v7',
        SetId binary(16) NOT NULL COMMENT '所属评估集',
        RevisionId binary(16) NOT NULL COMMENT '明确审核的不可变修订',
        ReviewerUserId binary(16) NOT NULL COMMENT '独立审核操作者',
        DecisionKey varchar(16) NOT NULL COMMENT 'approved 或 withdrawn，不代表模型已执行',
        RequestKey binary(16) NOT NULL COMMENT '审核幂等请求 UUID v7',
        RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '绑定原头与审核人的请求摘要',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '审核时间 UTC',
        CONSTRAINT PK_fn_ai_knowledge_evaluation_approval PRIMARY KEY (Id),
        CONSTRAINT CK_fn_ai_knowledge_evaluation_approval_Bounds CHECK (DecisionKey IN ('approved','withdrawn')),
        UNIQUE INDEX UX_fn_ai_knowledge_evaluation_approval_SetId_RequestKey (SetId,RequestKey)
    ) COMMENT='准确冻结修订的独立审核追加记录' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
