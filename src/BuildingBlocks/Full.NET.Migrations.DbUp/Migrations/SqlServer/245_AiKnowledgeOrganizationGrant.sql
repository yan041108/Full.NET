-- 245：机构仅保存权威标识，不向 Organization 建立外键；当前应用继续支持旧用户名单。
-- 245：机构授予仅属于本模块知识库；不向 Organization 或 Tenancy 建立外键。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_org', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_org (
        Id uniqueidentifier NOT NULL,
        KnowledgeBaseId uniqueidentifier NOT NULL,
        OrganizationUnitId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_org PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_org_KnowledgeBase FOREIGN KEY (KnowledgeBaseId) REFERENCES dbo.fn_ai_knowledge_base (Id),
        CONSTRAINT UQ_fn_ai_knowledge_org_BaseUnit UNIQUE (KnowledgeBaseId, OrganizationUnitId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_knowledge_org') AND name = N'IX_fn_ai_knowledge_org_UnitBase')
    CREATE INDEX IX_fn_ai_knowledge_org_UnitBase ON dbo.fn_ai_knowledge_org (OrganizationUnitId, KnowledgeBaseId);
GO
-- 注释独立收敛，允许 DDL 已提交但 DbUp 未记账的重放。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_org') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库机构授予', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_org';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_org') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_org'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_org', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_org') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_org'), N'KnowledgeBaseId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属知识库标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_org', @level2type=N'COLUMN', @level2name=N'KnowledgeBaseId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_org') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_org'), N'OrganizationUnitId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获授权机构标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_org', @level2type=N'COLUMN', @level2name=N'OrganizationUnitId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_org') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_org'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_org', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO

-- 245：机构授予仅属于本模块知识库；不向 Organization 或 Tenancy 建立外键。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_document_org', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_document_org (
        Id uniqueidentifier NOT NULL,
        DocumentId uniqueidentifier NOT NULL,
        OrganizationUnitId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_document_org PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_document_org_Document FOREIGN KEY (DocumentId) REFERENCES dbo.fn_ai_knowledge_document (Id),
        CONSTRAINT UQ_fn_ai_knowledge_document_org_DocUnit UNIQUE (DocumentId, OrganizationUnitId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_org') AND name = N'IX_fn_ai_knowledge_document_org_UnitDoc')
    CREATE INDEX IX_fn_ai_knowledge_document_org_UnitDoc ON dbo.fn_ai_knowledge_document_org (OrganizationUnitId, DocumentId);
GO
-- 注释独立收敛，允许 DDL 已提交但 DbUp 未记账的重放。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_org') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档机构授予', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_org';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_org') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_org'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_org', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_org') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_org'), N'DocumentId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_org', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_org') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_org'), N'OrganizationUnitId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获授权机构标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_org', @level2type=N'COLUMN', @level2name=N'OrganizationUnitId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_document_org') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_document_org'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_document_org', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
