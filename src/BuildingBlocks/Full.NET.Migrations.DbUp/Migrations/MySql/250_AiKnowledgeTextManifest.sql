-- 250：InnoDB 原子创建任务清单及同模块外键，重放不覆盖清单；旧任务不回填，不推进 ready。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_text_manifest (
    Id binary(16) NOT NULL COMMENT '逻辑主键及所属索引任务标识',
    FormatKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定清单格式版本',
    PayloadHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整清单载荷 SHA256 摘要',
    Payload mediumblob NOT NULL COMMENT '不含正文的完整有界文本批次清单',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_text_manifest PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_text_manifest_Id FOREIGN KEY (Id) REFERENCES fn_ai_knowledge_index_task (Id),
    CONSTRAINT CK_fn_ai_knowledge_text_manifest_Bounds CHECK (FormatKey='knowledge-text-manifest-v1' AND OCTET_LENGTH(Payload) BETWEEN 8 AND 131072)
) COMMENT='知识库索引任务不可替换的完整文本计划' ENGINE=InnoDB;
