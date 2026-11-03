-- 本地校对准备持久化；各 DDL 及注释允许未记账重跑。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_correction_preparation (
        Id uniqueidentifier NOT NULL,
        VersionId uniqueidentifier NOT NULL,
        DocumentVersion int NOT NULL,
        RequestKey uniqueidentifier NOT NULL,
        RequestHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        SessionKind varchar(32) NOT NULL,
        SecurityStamp nvarchar(128) NOT NULL,
        ActorScope varchar(64) NOT NULL,
        EffectiveScope varchar(64) NOT NULL,
        StateKey varchar(16) NOT NULL,
        AttemptCount int NOT NULL,
        LeaseToken uniqueidentifier NULL,
        LeaseExpiresAtUtc datetime2(6) NULL,
        ParserRuntimeId varchar(128) NULL,
        SourceId uniqueidentifier NULL,
        FailureCode varchar(96) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_correction_preparation PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ai_knowledge_correction_preparation_Bounds CHECK (DocumentVersion>0 AND AttemptCount BETWEEN 0 AND 3 AND StateKey IN ('queued','running','completed','failed','cancelled') AND ((StateKey='running' AND LeaseToken IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL AND ParserRuntimeId IS NOT NULL AND AttemptCount>0) OR (StateKey<>'running' AND LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL)) AND ((StateKey='completed' AND SourceId IS NOT NULL AND ParserRuntimeId IS NOT NULL AND AttemptCount>0) OR (StateKey<>'completed' AND SourceId IS NULL)) AND ((StateKey IN ('failed','cancelled') AND FailureCode IS NOT NULL) OR (StateKey NOT IN ('failed','cancelled') AND FailureCode IS NULL)))
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND name=N'UX_fn_ai_knowledge_correction_preparation_VersionId_RequestKey')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_correction_preparation_VersionId_RequestKey ON dbo.fn_ai_knowledge_correction_preparation (VersionId,RequestKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND name=N'IX_fn_ai_knowledge_correction_preparation_StateKey_Leas_e302433e')
    CREATE INDEX IX_fn_ai_knowledge_correction_preparation_StateKey_Leas_e302433e ON dbo.fn_ai_knowledge_correction_preparation (StateKey,LeaseExpiresAtUtc,CreatedAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地校对来源准备队列，仅解析不收费不发布',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'准备任务 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'VersionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'被冻结的原文件版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'VersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'DocumentVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'入队时文档维护版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'DocumentVersion';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'RequestKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'客户端原请求重试身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'RequestKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'完整资源及原会话请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'ActorUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原交互操作者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'SessionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原交互会话，不接受接管',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'SessionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'SessionKind','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原交互会话类型',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'SessionKind';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'SecurityStamp','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原会话安全戳，不进入公开响应',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'SecurityStamp';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'ActorScope','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原会话声明范围',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'ActorScope';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'EffectiveScope','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'原会话有效范围',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'EffectiveScope';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'StateKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'queued running completed failed cancelled',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'StateKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'AttemptCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'领取次数，最多三次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'AttemptCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'LeaseToken','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前领取身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'LeaseToken';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'LeaseExpiresAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'租约到期时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'LeaseExpiresAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'ParserRuntimeId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首次领取冻结的服务端解析运行时',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'ParserRuntimeId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'SourceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'同一事务完成的原解析快照',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'SourceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'FailureCode','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'受控失败机器码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'FailureCode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'入队时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_correction_preparation'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近状态变化时间 UTC',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_correction_preparation',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
GO
