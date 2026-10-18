-- 272：显式有界增量收费授权；expand 新表，不创建授权、不启动 Worker、不调用模型。
-- 回退应用前先关闭派发开关；保留授权及未知费用记录。每项 DDL 独立探测，可恢复未记账的部分部署。
IF OBJECT_ID('dbo.fn_ai_index_grant','U') IS NULL
BEGIN
CREATE TABLE dbo.fn_ai_index_grant (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NULL,
    KnowledgeBaseId uniqueidentifier NOT NULL,
    OwnerUserId uniqueidentifier NOT NULL,
    ClientRequestId uniqueidentifier NOT NULL,
    KnowledgeBaseVersion int NOT NULL,
    ModelConfigId uniqueidentifier NOT NULL,
    ModelConfigVersion int NOT NULL,
    SessionJson nvarchar(max) NOT NULL,
    ExpiresAtUtc datetimeoffset(7) NOT NULL,
    DocumentLimit int NOT NULL,
    AdmittedDocuments int NOT NULL,
    InputTokenLimitPerDocument bigint NOT NULL,
    IsRevoked bit NOT NULL,
    Version int NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_ai_index_grant PRIMARY KEY CLUSTERED (Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所有者原会话的有界增量收费授权；默认关闭且不可接管',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'授权逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'TenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户；宿主为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'KnowledgeBaseId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本模块知识库标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'KnowledgeBaseId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'OwnerUserId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原授权所有者标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'OwnerUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'ClientRequestId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所有者在知识库内的幂等键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'ClientRequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'KnowledgeBaseVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结的知识库及模型批准修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'KnowledgeBaseVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'ModelConfigId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原批准向量模型配置',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'ModelConfigId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'ModelConfigVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原批准模型配置修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'ModelConfigVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'SessionJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'冻结原交互会话绑定；不包含访问令牌且不可替换',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'SessionJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'ExpiresAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原授权到期时间 UTC；不得隐式延长',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'ExpiresAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'DocumentLimit','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最多准入任务数；失败及未知不返还',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'DocumentLimit';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'AdmittedDocuments','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已消耗的任务准入次数',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'AdmittedDocuments';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'InputTokenLimitPerDocument','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'每任务累计输入预留上限；不是费用硬限',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'InputTokenLimitPerDocument';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'IsRevoked','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'是否已由原所有者明确撤销',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'IsRevoked';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'Version','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准入和撤销共享并发修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant'),'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'授权创建时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_index_grant') AND name='UX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_ClientRequestId')
 ALTER TABLE dbo.fn_ai_index_grant ADD CONSTRAINT UX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_ClientRequestId UNIQUE (KnowledgeBaseId,OwnerUserId,ClientRequestId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_index_grant') AND name='CK_fn_ai_index_grant_State')
 ALTER TABLE dbo.fn_ai_index_grant ADD CONSTRAINT CK_fn_ai_index_grant_State CHECK (KnowledgeBaseVersion>=1 AND ModelConfigVersion>=1 AND DocumentLimit BETWEEN 1 AND 64 AND AdmittedDocuments>=0 AND AdmittedDocuments<=DocumentLimit AND InputTokenLimitPerDocument BETWEEN 1 AND 1000000 AND Version>=1 AND IsRevoked IN (0,1) AND DATALENGTH(SessionJson) BETWEEN 2 AND 8192);
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_index_grant') AND name='FK_fn_ai_index_grant_KnowledgeBaseId')
 ALTER TABLE dbo.fn_ai_index_grant ADD CONSTRAINT FK_fn_ai_index_grant_KnowledgeBaseId FOREIGN KEY (KnowledgeBaseId) REFERENCES fn_ai_knowledge_base (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_index_grant') AND name='FK_fn_ai_index_grant_ModelConfigId')
 ALTER TABLE dbo.fn_ai_index_grant ADD CONSTRAINT FK_fn_ai_index_grant_ModelConfigId FOREIGN KEY (ModelConfigId) REFERENCES fn_ai_model_config (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_ai_index_grant') AND name='IX_fn_ai_index_grant_IsRevoked_ExpiresAtUtc_Id')
 CREATE INDEX IX_fn_ai_index_grant_IsRevoked_ExpiresAtUtc_Id ON dbo.fn_ai_index_grant (IsRevoked,ExpiresAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_ai_index_grant') AND name='IX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_Id')
 CREATE INDEX IX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_Id ON dbo.fn_ai_index_grant (KnowledgeBaseId,OwnerUserId,Id);
GO
IF OBJECT_ID('dbo.fn_ai_index_grant_item','U') IS NULL
BEGIN
CREATE TABLE dbo.fn_ai_index_grant_item (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NULL,
    GrantId uniqueidentifier NOT NULL,
    TaskId uniqueidentifier NOT NULL,
    LastBatchOrdinal int NOT NULL,
    ReservedInputTokens bigint NOT NULL,
    CONSTRAINT PK_fn_ai_index_grant_item PRIMARY KEY CLUSTERED (Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'增量授权与原索引任务的不可替换绑定及累计输入预留',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant_item';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant_item'),'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant_item',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant_item'),'TenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'与原授权一致的可信租户；宿主为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant_item',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant_item'),'GrantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原增量收费授权标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant_item',@level2type=N'COLUMN',@level2name=N'GrantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant_item'),'TaskId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'唯一原索引任务标识；禁止接管其他代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant_item',@level2type=N'COLUMN',@level2name=N'TaskId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant_item'),'LastBatchOrdinal','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已顺序预留的最后批次序号；初始为负一',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant_item',@level2type=N'COLUMN',@level2name=N'LastBatchOrdinal';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_index_grant_item'),'ReservedInputTokens','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本任务已消耗的输入预留总量；不因未知或失败返还',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_index_grant_item',@level2type=N'COLUMN',@level2name=N'ReservedInputTokens';
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND name='UX_fn_ai_index_grant_item_TaskId')
 ALTER TABLE dbo.fn_ai_index_grant_item ADD CONSTRAINT UX_fn_ai_index_grant_item_TaskId UNIQUE (TaskId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND name='CK_fn_ai_index_grant_item_State')
 ALTER TABLE dbo.fn_ai_index_grant_item ADD CONSTRAINT CK_fn_ai_index_grant_item_State CHECK (LastBatchOrdinal BETWEEN -1 AND 4095 AND ReservedInputTokens BETWEEN 0 AND 1000000);
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND name='FK_fn_ai_index_grant_item_GrantId')
 ALTER TABLE dbo.fn_ai_index_grant_item ADD CONSTRAINT FK_fn_ai_index_grant_item_GrantId FOREIGN KEY (GrantId) REFERENCES fn_ai_index_grant (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND name='FK_fn_ai_index_grant_item_TaskId')
 ALTER TABLE dbo.fn_ai_index_grant_item ADD CONSTRAINT FK_fn_ai_index_grant_item_TaskId FOREIGN KEY (TaskId) REFERENCES fn_ai_knowledge_index_task (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_ai_index_grant_item') AND name='IX_fn_ai_index_grant_item_GrantId')
 CREATE INDEX IX_fn_ai_index_grant_item_GrantId ON dbo.fn_ai_index_grant_item (GrantId);
GO
