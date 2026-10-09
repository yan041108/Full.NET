-- 258：仅审核固定标签，不保存或释放正文；反馈修改或撤回使旧审核绑定失效。
-- 重放恢复结构和说明，不重置审核版本；应用回退保留数据，删除反馈级联清理。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review',N'U') IS NULL
BEGIN
CREATE TABLE dbo.fn_ai_knowledge_answer_review (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NULL,
    FeedbackId uniqueidentifier NOT NULL,
    FeedbackVersion int NOT NULL,
    LineageHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ReviewerUserId uniqueidentifier NOT NULL,
    DecisionKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Version int NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    UpdatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_ai_knowledge_answer_review PRIMARY KEY CLUSTERED (Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND name=N'UX_fn_ai_knowledge_answer_review_FeedbackId')
    ALTER TABLE dbo.fn_ai_knowledge_answer_review ADD CONSTRAINT UX_fn_ai_knowledge_answer_review_FeedbackId UNIQUE (FeedbackId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND name=N'FK_fn_ai_knowledge_answer_review_FeedbackId')
    ALTER TABLE dbo.fn_ai_knowledge_answer_review WITH CHECK ADD CONSTRAINT FK_fn_ai_knowledge_answer_review_FeedbackId FOREIGN KEY (FeedbackId) REFERENCES dbo.fn_ai_knowledge_answer_feedback (Id) ON DELETE CASCADE;
GO
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND name=N'CK_fn_ai_knowledge_answer_review_State')
    ALTER TABLE dbo.fn_ai_knowledge_answer_review WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_answer_review_State CHECK (Version>=1 AND FeedbackVersion>=1 AND DecisionKey IN ('approved','rejected') AND DATALENGTH(DecisionKey)=8 AND DATALENGTH(LineageHash)=64 AND LineageHash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2);
GO
ALTER TABLE dbo.fn_ai_knowledge_answer_review WITH CHECK CHECK CONSTRAINT FK_fn_ai_knowledge_answer_review_FeedbackId;
GO
ALTER TABLE dbo.fn_ai_knowledge_answer_review WITH CHECK CHECK CONSTRAINT CK_fn_ai_knowledge_answer_review_State;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'独立反馈标签审核；绑定反馈版本与来源，删除反馈级联清理',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'审核逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户；宿主为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'FeedbackId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'同模块反馈标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'FeedbackId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'FeedbackVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次审核的反馈版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'FeedbackVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'LineageHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次审核的完整来源摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'LineageHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'ReviewerUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'独立评审者标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'ReviewerUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'DecisionKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定批准或拒绝机器码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'DecisionKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'单调递增审核并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'Version';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时间（UTC）',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_answer_review'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'更新时间（UTC）',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_answer_review',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
