-- 249：一个批次一个原子载荷，不回填旧任务、不改 ready，不跨模块建立外键。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_index_vector (
        Id uniqueidentifier NOT NULL,
        ProviderKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ModelId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Dimensions int NOT NULL,
        FormatKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DistanceKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PayloadHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Payload varbinary(max) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_index_vector PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_index_vector_Id FOREIGN KEY (Id) REFERENCES dbo.fn_ai_knowledge_index_batch (Id),
        CONSTRAINT CK_fn_ai_knowledge_index_vector_Bounds CHECK (Dimensions IN (1536,3072) AND FormatKey='knowledge-vector-batch-v1' AND DistanceKey='cosine' AND DATALENGTH(Payload) BETWEEN 8 AND 524288)
    );
END;
GO
-- 表及列说明独立探测，允许创建成功但 DbUp 未记账时继续恢复。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库已结算文本向量批次派生数据', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键及所属预算批次标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'ProviderKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'冻结模型提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'ProviderKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'ModelId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'冻结模型标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'ModelId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'Dimensions','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'向量维度', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'Dimensions';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'FormatKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定载荷格式版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'FormatKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'DistanceKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定余弦距离规则', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'DistanceKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'PayloadHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整载荷 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'PayloadHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'Payload','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整有界片段与小端向量载荷', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'Payload';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
