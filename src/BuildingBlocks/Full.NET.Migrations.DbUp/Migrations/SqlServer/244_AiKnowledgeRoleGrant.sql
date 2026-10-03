-- 244：角色仅保存权威标识，不向 Identity 建立外键；当前应用继续支持旧用户名单。
-- 244：角色授予仅属于本模块知识库；不向 Identity 或 Tenancy 建立外键。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_role', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_role (
        Id uniqueidentifier NOT NULL,
        KnowledgeBaseId uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_role PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_role_KnowledgeBase FOREIGN KEY (KnowledgeBaseId) REFERENCES dbo.fn_ai_knowledge_base (Id),
        CONSTRAINT UQ_fn_ai_knowledge_role_BaseRole UNIQUE (KnowledgeBaseId, RoleId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_knowledge_role') AND name = N'IX_fn_ai_knowledge_role_RoleBase')
    CREATE INDEX IX_fn_ai_knowledge_role_RoleBase ON dbo.fn_ai_knowledge_role (RoleId, KnowledgeBaseId);
GO
-- 注释独立收敛，允许 DDL 已提交但 DbUp 未记账的重放。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_role') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库角色授予', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_role';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_role') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_role'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_role', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_role') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_role'), N'KnowledgeBaseId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属知识库标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_role', @level2type=N'COLUMN', @level2name=N'KnowledgeBaseId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_role') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_role'), N'RoleId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获授权角色标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_role', @level2type=N'COLUMN', @level2name=N'RoleId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_role') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_role'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_role', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO

-- 244：角色授予仅属于本模块知识库；不向 Identity 或 Tenancy 建立外键。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_document_role', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_document_role (
        Id uniqueidentifier NOT NULL,
        DocumentId uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_document_role PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_document_role_Document FOREIGN KEY (DocumentId) REFERENCES dbo.fn_ai_knowledge_document (Id),
        CONSTRAINT UQ_fn_ai_knowledge_document_role_DocRole UNIQUE (DocumentId, RoleId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_role') AND name = N'IX_fn_ai_knowledge_document_role_RoleDoc')
    CREATE INDEX IX_fn_ai_knowledge_document_role_RoleDoc ON dbo.fn_ai_knowledge_document_role (RoleId, DocumentId);
GO
-- 注释独立收敛，允许 DDL 已提交但 DbUp 未记账的重放。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_role') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档角色授予', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_role';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_role') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_role'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_role', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_role') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_role'), N'DocumentId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_role', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_role') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_role'), N'RoleId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获授权角色标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_role', @level2type=N'COLUMN', @level2name=N'RoleId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_role') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_role'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_role', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
