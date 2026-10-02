-- 240：私有知识库目录与默认拒绝的精确模型处理审批；只扩展结构。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_base', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_base (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NULL,
    OwnerUserId uniqueidentifier NOT NULL,
    Name nvarchar(200) NOT NULL,
    Description nvarchar(2000) NULL,
    IsEnabled bit NOT NULL,
    DataClassification varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    EmbeddingModelConfigId uniqueidentifier NULL,
    EmbeddingModelVersion int NULL,
    GenerationModelConfigId uniqueidentifier NULL,
    GenerationModelVersion int NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    UpdatedAtUtc datetimeoffset(7) NULL,
    Version int NOT NULL,
    CONSTRAINT PK_fn_ai_knowledge_base PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_fn_ai_knowledge_base_Classification CHECK (DataClassification IN ('public', 'internal', 'restricted')),
    CONSTRAINT CK_fn_ai_knowledge_base_EmbeddingApproval CHECK ((EmbeddingModelConfigId IS NULL AND EmbeddingModelVersion IS NULL) OR (EmbeddingModelConfigId IS NOT NULL AND EmbeddingModelVersion IS NOT NULL AND EmbeddingModelVersion > 0)),
    CONSTRAINT CK_fn_ai_knowledge_base_GenerationApproval CHECK ((GenerationModelConfigId IS NULL AND GenerationModelVersion IS NULL) OR (GenerationModelConfigId IS NOT NULL AND GenerationModelVersion IS NOT NULL AND GenerationModelVersion > 0)),
    CONSTRAINT CK_fn_ai_knowledge_base_Version CHECK (Version > 0)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND name = N'IX_fn_ai_knowledge_base_OwnerScope')
    CREATE INDEX IX_fn_ai_knowledge_base_OwnerScope ON dbo.fn_ai_knowledge_base (TenantId, OwnerUserId, CreatedAtUtc DESC, Id DESC);
GO
-- 索引与注释独立收敛，允许 CREATE TABLE 已提交但 DbUp 尚未记账的重放。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'私有知识库目录及模型处理审批', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'OwnerUserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库所有者标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'OwnerUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'Name', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'Name';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'Description', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'Description';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'IsEnabled', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'IsEnabled';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'DataClassification', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据分类机器码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'DataClassification';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'EmbeddingModelConfigId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获批向量模型配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'EmbeddingModelConfigId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'EmbeddingModelVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获批向量模型配置版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'EmbeddingModelVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'GenerationModelConfigId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获批生成模型配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'GenerationModelConfigId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'GenerationModelVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获批生成模型配置版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'GenerationModelVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_base') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_base'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目录与审批乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_base', @level2type=N'COLUMN', @level2name=N'Version';
GO
