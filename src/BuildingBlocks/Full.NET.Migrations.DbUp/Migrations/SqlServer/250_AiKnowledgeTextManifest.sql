-- 250：一个任务一个完整清单；不回填旧任务、不改 ready，仅建立 AI 模块内外键。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_text_manifest (
        Id uniqueidentifier NOT NULL,
        FormatKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PayloadHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Payload varbinary(max) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_text_manifest PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_text_manifest_Id FOREIGN KEY (Id) REFERENCES dbo.fn_ai_knowledge_index_task (Id),
        CONSTRAINT CK_fn_ai_knowledge_text_manifest_Bounds CHECK (FormatKey='knowledge-text-manifest-v1' AND DATALENGTH(Payload) BETWEEN 8 AND 131072)
    );
END;
GO
-- 说明独立探测，允许建表成功但 DbUp 未记账时继续恢复，不触碰原载荷。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库索引任务不可替换的完整文本计划', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_text_manifest';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键及所属索引任务标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_text_manifest', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest'),N'FormatKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定清单格式版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_text_manifest', @level2type=N'COLUMN', @level2name=N'FormatKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest'),N'PayloadHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整清单载荷 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_text_manifest', @level2type=N'COLUMN', @level2name=N'PayloadHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest'),N'Payload','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不含正文的完整有界文本批次清单', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_text_manifest', @level2type=N'COLUMN', @level2name=N'Payload';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_text_manifest', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
