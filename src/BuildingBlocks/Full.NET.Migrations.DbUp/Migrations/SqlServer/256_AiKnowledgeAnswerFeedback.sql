-- 256：仅保存固定标签和引用标识；不保存答案正文，不自动进入评估或模型输入。
-- 重放只恢复结构与说明；应用回退保留数据，物理删除消息级联清理本人反馈。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback',N'U') IS NULL
BEGIN
CREATE TABLE dbo.fn_ai_knowledge_answer_feedback (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NULL,
    SessionId uniqueidentifier NOT NULL,
    MessageId uniqueidentifier NOT NULL,
    ActorUserId uniqueidentifier NOT NULL,
    RatingKey varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
    ReasonKey varchar(32) COLLATE Latin1_General_100_BIN2 NULL,
    CitationIdsJson varchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL,
    LineageHash char(64) COLLATE Latin1_General_100_BIN2 NULL,
    StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Version int NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    UpdatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_ai_knowledge_answer_feedback PRIMARY KEY CLUSTERED (Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND name=N'UX_fn_ai_knowledge_answer_feedback_MessageId')
    ALTER TABLE dbo.fn_ai_knowledge_answer_feedback ADD CONSTRAINT UX_fn_ai_knowledge_answer_feedback_MessageId UNIQUE (MessageId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND name=N'FK_fn_ai_knowledge_answer_feedback_MessageId')
    ALTER TABLE dbo.fn_ai_knowledge_answer_feedback WITH CHECK ADD CONSTRAINT FK_fn_ai_knowledge_answer_feedback_MessageId FOREIGN KEY (MessageId) REFERENCES dbo.fn_ai_chat_message (Id) ON DELETE CASCADE;
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND name=N'CK_fn_ai_knowledge_answer_feedback_State')
    ALTER TABLE dbo.fn_ai_knowledge_answer_feedback WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_answer_feedback_State CHECK (Version>=1 AND ISJSON(CitationIdsJson, ARRAY)=1 AND ((StatusKey='pending_review' AND DATALENGTH(StatusKey)=14 AND RatingKey IS NOT NULL AND ReasonKey IS NOT NULL AND LineageHash IS NOT NULL AND DATALENGTH(LineageHash)=64 AND LineageHash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 AND ((RatingKey='helpful' AND DATALENGTH(RatingKey)=7 AND ReasonKey IN ('accurate','useful')) OR (RatingKey='unhelpful' AND DATALENGTH(RatingKey)=9 AND ReasonKey IN ('incomplete','unsupported','outdated','other'))) AND DATALENGTH(ReasonKey)=CASE ReasonKey WHEN 'accurate' THEN 8 WHEN 'useful' THEN 6 WHEN 'incomplete' THEN 10 WHEN 'unsupported' THEN 11 WHEN 'outdated' THEN 8 WHEN 'other' THEN 5 ELSE 0 END) OR (StatusKey='withdrawn' AND DATALENGTH(StatusKey)=9 AND RatingKey IS NULL AND ReasonKey IS NULL AND LineageHash IS NULL AND CitationIdsJson='[]' AND DATALENGTH(CitationIdsJson)=2)));
GO
ALTER TABLE dbo.fn_ai_knowledge_answer_feedback WITH CHECK CHECK CONSTRAINT FK_fn_ai_knowledge_answer_feedback_MessageId;
GO
ALTER TABLE dbo.fn_ai_knowledge_answer_feedback WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_answer_feedback_State;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本人知识答案未审核反馈；撤回清空载荷，删除消息级联清理',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'反馈逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户；宿主为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'SessionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本人会话标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'SessionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'MessageId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际已完成知识答案标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'MessageId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'提交用户标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'RatingKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定评价机器码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'RatingKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'ReasonKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定原因机器码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'ReasonKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'CitationIdsJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'实际答案引用标识数组',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'CitationIdsJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'LineageHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'完整答案依赖 SHA256 摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'LineageHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'StatusKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'待审核或已撤回状态',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'乐观并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'Version';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间（UTC）',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_feedback'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'更新时间（UTC）',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_feedback',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
