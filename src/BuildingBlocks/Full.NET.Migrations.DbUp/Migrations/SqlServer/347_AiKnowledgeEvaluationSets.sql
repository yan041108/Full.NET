-- 不可变人工脱敏评估集；仅新增 AI 自有表，不创建文件、调用模型或发布知识。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_evaluation_set (
        Id uniqueidentifier NOT NULL,
        KnowledgeBaseId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        CreatedById uniqueidentifier NOT NULL,
        RequestKey uniqueidentifier NOT NULL,
        CreateHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        HeadRevisionId uniqueidentifier NOT NULL,
        HeadRevisionNo int NOT NULL,
        Version int NOT NULL,
        FootprintJson nvarchar(max) NOT NULL,
        FootprintHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ApprovalId uniqueidentifier NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_evaluation_set PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ai_knowledge_evaluation_set_Bounds CHECK (HeadRevisionNo>0 AND Version>0)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'知识库评估集根，当前头与累计不可降级来源',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'评估集 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'KnowledgeBaseId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属知识库',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'KnowledgeBaseId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户；NULL 表示 Host',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'CreatedById','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原始创建者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'CreatedById';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'RequestKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建幂等请求 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'RequestKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'CreateHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建请求与操作者完整摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'CreateHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'HeadRevisionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前不可变修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'HeadRevisionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'HeadRevisionNo','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前修订序号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'HeadRevisionNo';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'根乐观并发版本，批准也递增',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'Version';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'FootprintJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'累计完整来源与正文贡献者，禁止清空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'FootprintJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'FootprintHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'累计来源规范 JSON 摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'FootprintHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'ApprovalId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前修订最近独立审核记录',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'ApprovalId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_set',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_set') AND name=N'UX_fn_ai_knowledge_evaluation_set_KnowledgeBaseId_RequestKey')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_evaluation_set_KnowledgeBaseId_RequestKey ON dbo.fn_ai_knowledge_evaluation_set (KnowledgeBaseId,RequestKey);
GO
IF OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_evaluation_revision (
        Id uniqueidentifier NOT NULL,
        SetId uniqueidentifier NOT NULL,
        RevisionNo int NOT NULL,
        RequestKey uniqueidentifier NOT NULL,
        RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ContentJson nvarchar(max) NOT NULL,
        ContentHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceJson nvarchar(max) NOT NULL,
        SourceHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        EditorUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        RestoredFromRevisionId uniqueidentifier NULL,
        CONSTRAINT PK_fn_ai_knowledge_evaluation_revision PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ai_knowledge_evaluation_revision_Bounds CHECK (RevisionNo>0)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'人工脱敏评估不可变修订，仅追加不覆盖',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'修订 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'SetId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属评估集',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'SetId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'RevisionNo','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'从一开始的修订次序',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'RevisionNo';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'RequestKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次追加的幂等请求 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'RequestKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定原头和操作者的请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'ContentJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'人工脱敏正文及显式草稿或冻结状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'ContentJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'ContentHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'正文与该修订来源摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'ContentHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'SourceJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'该修订不可变累计来源和贡献者足迹',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'SourceJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'SourceHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'该修订来源摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'SourceHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'EditorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'该修订编辑者，不能批准自身正文',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'EditorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'修订创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision'),N'RestoredFromRevisionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'明确恢复的本评估集历史修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_revision',@level2type=N'COLUMN',@level2name=N'RestoredFromRevisionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND name=N'UX_fn_ai_knowledge_evaluation_revision_SetId_RequestKey')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_evaluation_revision_SetId_RequestKey ON dbo.fn_ai_knowledge_evaluation_revision (SetId,RequestKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_revision') AND name=N'UX_fn_ai_knowledge_evaluation_revision_SetId_RevisionNo')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_evaluation_revision_SetId_RevisionNo ON dbo.fn_ai_knowledge_evaluation_revision (SetId,RevisionNo);
GO
IF OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_evaluation_approval (
        Id uniqueidentifier NOT NULL,
        SetId uniqueidentifier NOT NULL,
        RevisionId uniqueidentifier NOT NULL,
        ReviewerUserId uniqueidentifier NOT NULL,
        DecisionKey varchar(16) NOT NULL,
        RequestKey uniqueidentifier NOT NULL,
        RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_evaluation_approval PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ai_knowledge_evaluation_approval_Bounds CHECK (DecisionKey IN ('approved','withdrawn'))
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准确冻结修订的独立审核追加记录',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'审核 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval'),N'SetId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属评估集',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval',@level2type=N'COLUMN',@level2name=N'SetId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval'),N'RevisionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'明确审核的不可变修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval',@level2type=N'COLUMN',@level2name=N'RevisionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval'),N'ReviewerUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'独立审核操作者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval',@level2type=N'COLUMN',@level2name=N'ReviewerUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval'),N'DecisionKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'approved 或 withdrawn，不代表模型已执行',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval',@level2type=N'COLUMN',@level2name=N'DecisionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval'),N'RequestKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'审核幂等请求 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval',@level2type=N'COLUMN',@level2name=N'RequestKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定原头与审核人的请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'审核时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_evaluation_approval',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_evaluation_approval') AND name=N'UX_fn_ai_knowledge_evaluation_approval_SetId_RequestKey')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_evaluation_approval_SetId_RequestKey ON dbo.fn_ai_knowledge_evaluation_approval (SetId,RequestKey);
GO
