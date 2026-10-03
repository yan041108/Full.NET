-- 241：明确成员仅属于本模块知识库；不向 Identity 或 Tenancy 建立外键。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_member', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_member (
        Id uniqueidentifier NOT NULL,
        KnowledgeBaseId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_member PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_member_KnowledgeBase FOREIGN KEY (KnowledgeBaseId) REFERENCES dbo.fn_ai_knowledge_base (Id),
        CONSTRAINT UQ_fn_ai_knowledge_member_BaseUser UNIQUE (KnowledgeBaseId, UserId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_knowledge_member') AND name = N'IX_fn_ai_knowledge_member_UserBase')
    CREATE INDEX IX_fn_ai_knowledge_member_UserBase ON dbo.fn_ai_knowledge_member (UserId, KnowledgeBaseId);
GO
-- 注释独立收敛，允许 DDL 已提交但 DbUp 未记账的重放。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_member') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库明确成员授权', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_member';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_member') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_member'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_member', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_member') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_member'), N'KnowledgeBaseId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属知识库标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_member', @level2type=N'COLUMN', @level2name=N'KnowledgeBaseId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_member') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_member'), N'UserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获授权用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_member', @level2type=N'COLUMN', @level2name=N'UserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_member') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_member'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_member', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
