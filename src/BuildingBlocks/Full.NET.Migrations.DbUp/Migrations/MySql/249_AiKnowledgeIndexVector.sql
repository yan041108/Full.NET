-- 249：InnoDB 原子创建批次载荷及本地约束；重放保留原向量和账本身份。
CREATE TABLE IF NOT EXISTS fn_ai_knowledge_index_vector (
    Id binary(16) NOT NULL COMMENT '逻辑主键及所属预算批次标识',
    ProviderKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '冻结模型提供程序键',
    ModelId varchar(256) COLLATE utf8mb4_bin NOT NULL COMMENT '冻结模型标识',
    Dimensions int NOT NULL COMMENT '向量维度',
    FormatKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定载荷格式版本',
    DistanceKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定余弦距离规则',
    PayloadHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整载荷 SHA256 摘要',
    Payload mediumblob NOT NULL COMMENT '完整有界片段与小端向量载荷',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间（UTC）',
    CONSTRAINT PK_fn_ai_knowledge_index_vector PRIMARY KEY (Id),
    CONSTRAINT FK_fn_ai_knowledge_index_vector_Id FOREIGN KEY (Id) REFERENCES fn_ai_knowledge_index_batch (Id),
    CONSTRAINT CK_fn_ai_knowledge_index_vector_Bounds CHECK (Dimensions IN (1536,3072) AND FormatKey='knowledge-vector-batch-v1' AND DistanceKey='cosine' AND OCTET_LENGTH(Payload) BETWEEN 8 AND 524288)
) COMMENT='知识库已结算文本向量批次派生数据' ENGINE=InnoDB;
