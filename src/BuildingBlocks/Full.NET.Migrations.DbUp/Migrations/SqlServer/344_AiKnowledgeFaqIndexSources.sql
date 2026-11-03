-- FAQ 显式数据库修订来源，不创建 Files 对象或伪造普通导入；各 DDL 可重复恢复。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_faq_source (
        Id uniqueidentifier NOT NULL,
        FaqId uniqueidentifier NOT NULL,
        RevisionId uniqueidentifier NOT NULL,
        RevisionHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DataClassification varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        KnowledgeBaseVersion int NOT NULL,
        ModelConfigId uniqueidentifier NOT NULL,
        ModelConfigVersion int NOT NULL,
        ModelFingerprint varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BodyText nvarchar(max) NOT NULL,
        BodyHash varchar(64) NOT NULL,
        SizeBytes bigint NOT NULL,
        RequestKey uniqueidentifier NOT NULL,
        RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_faq_source PRIMARY KEY CLUSTERED (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND name=N'UX_fn_ai_knowledge_faq_source_RevisionId')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_faq_source_RevisionId ON dbo.fn_ai_knowledge_faq_source (RevisionId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND name=N'UX_fn_ai_knowledge_faq_source_FaqId_RequestKey')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_faq_source_FaqId_RequestKey ON dbo.fn_ai_knowledge_faq_source (FaqId,RequestKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'FAQ 不可变修订的数据库索引来源，无文件或普通导入凭证',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'索引知识版本 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'FaqId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属 FAQ',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'FaqId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'RevisionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准确不可变 FAQ 修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'RevisionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'RevisionHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'修订正文及原始来源摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'RevisionHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'SourceHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变完整来源摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'SourceHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'DataClassification','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前来源交集最严格数据分类',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'DataClassification';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'BodyText','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'服务器确定性生成的修订索引文本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'BodyText';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'BodyHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'UTF-8 索引文本摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'BodyHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'SizeBytes','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'索引文本 UTF-8 字节数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'SizeBytes';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'RequestKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'索引准入幂等请求 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'RequestKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结修订头和明确收费确认的请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次请求可信主体',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_faq_release (
        Id uniqueidentifier NOT NULL,
        FaqId uniqueidentifier NOT NULL,
        RevisionId uniqueidentifier NOT NULL,
        VersionId uniqueidentifier NOT NULL,
        SourceHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ContentHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_faq_release PRIMARY KEY CLUSTERED (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'FAQ 审核发布绑定与普通发布协议隔离，指针原子推进',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'同一 AI 发布身份 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'),N'FaqId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属 FAQ',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release',@level2type=N'COLUMN',@level2name=N'FaqId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'),N'RevisionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结审核修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release',@level2type=N'COLUMN',@level2name=N'RevisionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'),N'VersionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结完整索引知识版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release',@level2type=N'COLUMN',@level2name=N'VersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'),N'SourceHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'完整原始来源摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release',@level2type=N'COLUMN',@level2name=N'SourceHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'),N'ContentHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'审核修订内容摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release',@level2type=N'COLUMN',@level2name=N'ContentHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'发布申请完整请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_release'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_release',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_faq_query (
        Id uniqueidentifier NOT NULL,
        KnowledgeBaseId uniqueidentifier NOT NULL,
        FaqId uniqueidentifier NOT NULL,
        QueryHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_faq_query PRIMARY KEY CLUSTERED (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query') AND name=N'UX_fn_ai_knowledge_faq_query_KnowledgeBaseId_QueryHash')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_faq_query_KnowledgeBaseId_QueryHash ON dbo.fn_ai_knowledge_faq_query (KnowledgeBaseId,QueryHash);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前发布 FAQ 问法的库内唯一占用；撤销或替换在同一事务释放',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_query';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'问法占用 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_query',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query'),N'KnowledgeBaseId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属知识库',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_query',@level2type=N'COLUMN',@level2name=N'KnowledgeBaseId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query'),N'FaqId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'拥有当前问法的 FAQ',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_query',@level2type=N'COLUMN',@level2name=N'FaqId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query'),N'QueryHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'版本一规范化完整问法摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_query',@level2type=N'COLUMN',@level2name=N'QueryHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_query'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'占用创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_query',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_version',N'FaqRevisionId') IS NULL ALTER TABLE dbo.fn_ai_knowledge_version ADD FaqRevisionId uniqueidentifier NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_version') AND name=N'FileId' AND is_nullable=0) ALTER TABLE dbo.fn_ai_knowledge_version ALTER COLUMN FileId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_version') AND name=N'CK_fn_ai_knowledge_version_SourceKind')
    ALTER TABLE dbo.fn_ai_knowledge_version WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_version_SourceKind CHECK ((FileId IS NOT NULL AND FaqRevisionId IS NULL) OR (FileId IS NULL AND FaqRevisionId IS NOT NULL));
GO
IF COL_LENGTH(N'dbo.fn_ai_knowledge_faq',N'PublicationId') IS NULL ALTER TABLE dbo.fn_ai_knowledge_faq ADD PublicationId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'KnowledgeBaseVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次索引批准的知识库版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'KnowledgeBaseVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'ModelConfigId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次索引批准的模型身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'ModelConfigId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'ModelConfigVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次索引批准的模型版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'ModelConfigVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source'),N'ModelFingerprint','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'包含目的地和受保护凭据的完整模型摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq_source',@level2type=N'COLUMN',@level2name=N'ModelFingerprint';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_version'),N'FaqRevisionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'FAQ 修订来源；与 FileId 二选一',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_version',@level2type=N'COLUMN',@level2name=N'FaqRevisionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq'),N'PublicationId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前发布身份；与正式发布头同事务推进',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq',@level2type=N'COLUMN',@level2name=N'PublicationId';
GO
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_version'),N'FileId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'真实 Files 引用；FAQ 数据库修订时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_version',@level2type=N'COLUMN',@level2name=N'FileId';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'真实 Files 引用；FAQ 数据库修订时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_version',@level2type=N'COLUMN',@level2name=N'FileId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq_source') AND name=N'CK_fn_ai_knowledge_faq_source_Bounds') ALTER TABLE dbo.fn_ai_knowledge_faq_source WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_faq_source_Bounds CHECK (SizeBytes BETWEEN 1 AND 131072 AND DATALENGTH(BodyText)<=262144 AND KnowledgeBaseVersion>0 AND ModelConfigVersion>0 AND DataClassification IN ('public','internal','restricted'));
GO
