-- 261：意图只表示来源需要索引；现有会话和费用门禁保持不变。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_index_change (
    Id BINARY(16) NOT NULL COMMENT '应用生成的 UUID v7 主键',
    DocumentId BINARY(16) NOT NULL COMMENT 'AI 文档标识；每个文档只保留最新来源意图',
    VersionId BINARY(16) NOT NULL COMMENT '最新已确认的不可变文件版本标识',
    VersionNumber INT NOT NULL COMMENT '单调递增的文件版本序号；重复和乱序消费不得回退',
    CreatedAtUtc DATETIME(6) NOT NULL COMMENT '首次创建增量意图的 UTC 时间',
    UpdatedAtUtc DATETIME(6) NOT NULL COMMENT '最新来源意图更新的 UTC 时间',
    CONSTRAINT PK_fn_ai_knowledge_index_change PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_knowledge_index_change_Version CHECK (VersionNumber>0),
    CONSTRAINT UQ_fn_ai_knowledge_index_change_Document UNIQUE (DocumentId)
) COMMENT='最新确认来源的增量索引意图；执行仍需当前会话授权' ENGINE=InnoDB;
