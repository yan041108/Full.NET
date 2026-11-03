-- 不可变策略和实际运行原身份；只创建 AI 自有表，不自动调用模型或替换生产策略。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_answer_observation (
        Id binary(16) NOT NULL COMMENT '应用端 UUID v7',
        TenantId binary(16) NULL COMMENT '可信租户，Host为空',
        KnowledgeBaseIdsJson mediumtext NOT NULL COMMENT '服务器捕获的候选知识库身份，最多八项',
        Refused tinyint(1) NOT NULL COMMENT '实际受限回答协议的拒答标记',
        DurationMs decimal(20,3) NULL COMMENT '实际收费知识流水线毫秒，未观测为空',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '记录创建 UTC 时间',
        CONSTRAINT PK_fn_ai_knowledge_answer_observation PRIMARY KEY (Id),
        CONSTRAINT CK_fn_ai_knowledge_answer_observation_Bounds CHECK ((DurationMs IS NULL OR DurationMs BETWEEN 0 AND 86400000))
    ) COMMENT='实际知识回答结果分类及单调时钟观测，不保存问题或答案' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
